using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Bhengu.AI.Voice;

/// <summary>
/// Captures raw audio from a platform input (microphone) and exposes it as
/// an asynchronous stream of PCM byte chunks. Implementations are expected
/// to produce data in the format reported by <see cref="Format"/>.
/// </summary>
public interface IAudioCapture : IAsyncDisposable
{
    /// <summary>The PCM format produced by <see cref="CaptureAsync"/>.</summary>
    AudioFormat Format { get; }

    /// <summary>
    /// Begin capturing audio. The returned sequence yields PCM chunks until
    /// the cancellation token is signalled or the underlying capture stops.
    /// </summary>
    /// <param name="ct">Cancellation token used to stop capture.</param>
    IAsyncEnumerable<ReadOnlyMemory<byte>> CaptureAsync(CancellationToken ct);
}

/// <summary>
/// No-op <see cref="IAudioCapture"/> that yields no audio. Used as a safe
/// default when no platform microphone backend is available.
/// </summary>
public sealed class NullAudioCapture : IAudioCapture
{
    /// <inheritdoc />
    public AudioFormat Format { get; } = AudioFormat.Pcm16Mono16k;

    /// <inheritdoc />
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> CaptureAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Payload describing a completed transcription produced by
/// <see cref="VoicePipeline"/> after a wake-word activation.
/// </summary>
public sealed class TranscribedEventArgs : EventArgs
{
    /// <summary>The final transcription result for the activation.</summary>
    public required TranscriptionResult Result { get; init; }

    /// <summary>UTC timestamp when the transcription completed.</summary>
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Convenience composition of <see cref="IWakeWordDetector"/>,
/// <see cref="IAudioCapture"/>, <see cref="IVoiceTranscriber"/>, and
/// optionally <see cref="IVoiceActivityDetector"/> and <see cref="ITtsEngine"/>.
/// On wake-word detection the pipeline starts capturing audio, optionally
/// filters it through VAD, feeds the speech chunks to the transcriber, and
/// raises <see cref="Transcribed"/> with the final <see cref="TranscriptionResult"/>.
/// </summary>
/// <remarks>
/// The pipeline does not own the wake-word lifecycle: callers must invoke
/// <see cref="StartAsync"/> to begin listening and <see cref="StopAsync"/>
/// to shut down. Disposing the pipeline disposes all collaborators.
/// </remarks>
public sealed class VoicePipeline : IAsyncDisposable
{
    private readonly IWakeWordDetector _wake;
    private readonly IVoiceTranscriber _transcriber;
    private readonly IAudioCapture _capture;
    private readonly IVoiceActivityDetector? _vad;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _activationCts;
    private bool _disposed;

    /// <summary>
    /// Construct a new pipeline.
    /// </summary>
    /// <param name="wake">Wake-word detector. Required.</param>
    /// <param name="transcriber">Voice transcriber. Required.</param>
    /// <param name="capture">
    /// Audio capture source. When <c>null</c>, a <see cref="NullAudioCapture"/>
    /// is used (no audio is fed to the transcriber).
    /// </param>
    /// <param name="vad">
    /// Optional voice activity detector. When non-null, raw audio is piped
    /// through <see cref="IVoiceActivityDetector.DetectAsync"/> and only chunks
    /// with <see cref="VadSegment.IsSpeech"/> set to <c>true</c> are forwarded
    /// to the transcriber. When <c>null</c>, all captured audio is forwarded
    /// directly (original behaviour).
    /// </param>
    /// <param name="tts">
    /// Optional TTS engine. When set, it is exposed via <see cref="TtsEngine"/>
    /// so that the host can synthesise responses after a transcription. The
    /// pipeline itself does not invoke TTS — that is the caller's responsibility.
    /// </param>
    public VoicePipeline(
        IWakeWordDetector wake,
        IVoiceTranscriber transcriber,
        IAudioCapture? capture = null,
        IVoiceActivityDetector? vad = null,
        ITtsEngine? tts = null)
    {
        ArgumentNullException.ThrowIfNull(wake);
        ArgumentNullException.ThrowIfNull(transcriber);

        _wake = wake;
        _transcriber = transcriber;
        _capture = capture ?? new NullAudioCapture();
        _vad = vad;
        TtsEngine = tts;
        _wake.WakeWordDetected += OnWakeWordDetected;
    }

    /// <summary>
    /// Raised when a wake-word activation produces a final transcription.
    /// </summary>
    public event EventHandler<TranscribedEventArgs>? Transcribed;

    /// <summary>
    /// Raised when an activation fails (capture, transcription, or
    /// cancellation error). Subscribers may inspect the exception.
    /// </summary>
    public event EventHandler<Exception>? ActivationFailed;

    /// <summary>The wake-word detector this pipeline observes.</summary>
    public IWakeWordDetector WakeDetector => _wake;

    /// <summary>The transcriber this pipeline drives.</summary>
    public IVoiceTranscriber Transcriber => _transcriber;

    /// <summary>The audio capture source this pipeline reads from.</summary>
    public IAudioCapture AudioCapture => _capture;

    /// <summary>
    /// The optional TTS engine supplied at construction. <c>null</c> when no
    /// TTS backend was provided. The host is responsible for calling
    /// <see cref="ITtsEngine.SynthesiseAsync"/> or
    /// <see cref="ITtsEngine.StreamSynthesiseAsync"/> after a transcription
    /// event to produce spoken responses.
    /// </summary>
    public ITtsEngine? TtsEngine { get; }

    /// <summary>
    /// The optional voice activity detector supplied at construction.
    /// <c>null</c> when VAD is not active (all audio forwarded to transcriber).
    /// </summary>
    public IVoiceActivityDetector? VoiceActivityDetector => _vad;

    /// <summary>
    /// Begin listening for the wake word. Delegates to
    /// <see cref="IWakeWordDetector.StartAsync"/>.
    /// </summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _wake.StartAsync(ct);
    }

    /// <summary>
    /// Stop listening for the wake word and cancel any in-flight activation.
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelActivation();
        await _wake.StopAsync(ct).ConfigureAwait(false);
    }

    private void OnWakeWordDetected(object? sender, WakeWordDetectedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        // Cancel any previous activation still running, then start a new one.
        CancelActivation();

        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            _activationCts = cts;
        }

        _ = Task.Run(() => RunActivationAsync(cts.Token), CancellationToken.None);
    }

    private async Task RunActivationAsync(CancellationToken ct)
    {
        try
        {
            // When VAD is configured, pipe raw audio through it and only pass
            // speech segments (IsSpeech == true) to the transcriber.  When VAD
            // is absent, forward the raw capture stream directly (original behaviour).
            IAsyncEnumerable<ReadOnlyMemory<byte>> audioInput = _vad is null
                ? _capture.CaptureAsync(ct)
                : ExtractSpeechSegmentsAsync(_vad, _capture.CaptureAsync(ct), ct);

            var result = await _transcriber
                .StreamTranscribeAsync(audioInput, ct)
                .ToFinalAsync(ct)
                .ConfigureAwait(false);

            if (result is not null)
            {
                Transcribed?.Invoke(this, new TranscribedEventArgs { Result = result });
            }
            else
            {
                // Transcriber stream yielded no final result — silence, noise, or premature cancel.
                // This is normal; no event is raised.
                Trace.TraceInformation("VoicePipeline: activation produced no final transcription (silent or empty audio).");
            }
        }
        catch (OperationCanceledException)
        {
            // Activation was cancelled (stop requested or new wake event). Swallow.
        }
        catch (Exception ex)
        {
            ActivationFailed?.Invoke(this, ex);
        }
    }

    /// <summary>
    /// Filters <paramref name="rawAudio"/> through <paramref name="vad"/> and
    /// yields only the audio bytes from segments where
    /// <see cref="VadSegment.IsSpeech"/> is <c>true</c>.
    /// </summary>
    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> ExtractSpeechSegmentsAsync(
        IVoiceActivityDetector vad,
        IAsyncEnumerable<ReadOnlyMemory<byte>> rawAudio,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var segment in vad.DetectAsync(rawAudio, ct).ConfigureAwait(false))
        {
            if (segment.IsSpeech)
            {
                yield return segment.Audio;
            }
        }
    }

    private void CancelActivation()
    {
        CancellationTokenSource? toCancel;
        lock (_gate)
        {
            toCancel = _activationCts;
            _activationCts = null;
        }

        if (toCancel is not null)
        {
            try
            {
                toCancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // already disposed
            }
            toCancel.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _wake.WakeWordDetected -= OnWakeWordDetected;
        CancelActivation();

        await _wake.DisposeAsync().ConfigureAwait(false);
        await _transcriber.DisposeAsync().ConfigureAwait(false);
        await _capture.DisposeAsync().ConfigureAwait(false);
    }
}

internal static class PartialTranscriptionAsyncEnumerableExtensions
{
    /// <summary>
    /// Drain the partial-transcription stream and return the final result.
    /// Returns <c>null</c> if the stream produces no items.
    /// </summary>
    internal static async Task<TranscriptionResult?> ToFinalAsync(
        this IAsyncEnumerable<PartialTranscription> source,
        CancellationToken ct)
    {
        PartialTranscription? last = null;
        await foreach (var partial in source.WithCancellation(ct).ConfigureAwait(false))
        {
            last = partial;
            if (partial.IsFinal)
            {
                break;
            }
        }

        if (last is null)
        {
            return null;
        }

        // We do not know the language at this layer; callers can use the
        // single-shot TranscribeAsync overload for richer metadata.
        return new TranscriptionResult(last.Text, last.Confidence, "und");
    }
}
