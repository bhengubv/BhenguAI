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
/// <see cref="IAudioCapture"/>, and <see cref="IVoiceTranscriber"/>.
/// On wake-word detection the pipeline starts capturing audio, feeds the
/// chunks to the transcriber, and raises <see cref="Transcribed"/> with the
/// final <see cref="TranscriptionResult"/>.
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
    public VoicePipeline(IWakeWordDetector wake, IVoiceTranscriber transcriber, IAudioCapture? capture = null)
    {
        ArgumentNullException.ThrowIfNull(wake);
        ArgumentNullException.ThrowIfNull(transcriber);

        _wake = wake;
        _transcriber = transcriber;
        _capture = capture ?? new NullAudioCapture();
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
            var result = await _transcriber
                .StreamTranscribeAsync(_capture.CaptureAsync(ct), ct)
                .ToFinalAsync(ct)
                .ConfigureAwait(false);

            if (result is not null)
            {
                Transcribed?.Invoke(this, new TranscribedEventArgs { Result = result });
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
