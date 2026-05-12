using Circle.AI.Companion;
using Circle.AI.Voice;

namespace Circle.AI.IoT;

/// <summary>
/// Voice-in → Companion → voice-out pipeline for IoT devices.
/// Wires the wake word detector, transcriber, Companion session, and TTS
/// engine into a single listening loop via <see cref="VoicePipeline"/>.
/// </summary>
public sealed class IoTCompanionPipeline : IAsyncDisposable
{
    private readonly ICompanionSession _session;
    private readonly VoicePipeline     _voicePipeline;
    private readonly ITtsEngine?       _tts;
    private bool _disposed;

    /// <summary>
    /// Raised when the Companion has synthesised a reply audio buffer ready
    /// for playback on the IoT speaker.
    /// </summary>
    public event EventHandler<TtsSynthesisResult>? AudioReady;

    public IoTCompanionPipeline(
        ICompanionSession session,
        IWakeWordDetector wakeWord,
        IVoiceTranscriber transcriber,
        IAudioCapture? audioCapture = null,
        ITtsEngine? tts = null)
    {
        _session        = session ?? throw new ArgumentNullException(nameof(session));
        _tts            = tts;
        _voicePipeline  = new VoicePipeline(wakeWord, transcriber, audioCapture, tts: tts);
        _voicePipeline.Transcribed += OnTranscribed;
    }

    /// <summary>Starts the wake-word listener. Non-blocking.</summary>
    public Task StartAsync(CancellationToken ct = default)
        => _voicePipeline.StartAsync(ct);

    /// <summary>Stops the wake-word listener.</summary>
    public Task StopAsync(CancellationToken ct = default)
        => _voicePipeline.StopAsync(ct);

    private void OnTranscribed(object? sender, TranscribedEventArgs e)
    {
        // Fire-and-forget on the thread pool so we don't block the pipeline event.
        _ = HandleTranscriptionAsync(e.Result.Text);
    }

    private async Task HandleTranscriptionAsync(string utterance)
    {
        if (string.IsNullOrWhiteSpace(utterance)) return;

        try
        {
            var reply = await _session.SendAsync(utterance).ConfigureAwait(false);

            if (_tts is not null)
            {
                var audio = await _tts.SynthesiseAsync(reply).ConfigureAwait(false);
                AudioReady?.Invoke(this, audio);
            }
        }
        catch { /* Swallow — IoT pipeline must never crash the device process. */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _voicePipeline.Transcribed -= OnTranscribed;
        await _voicePipeline.DisposeAsync().ConfigureAwait(false);
        await _session.DisposeAsync().ConfigureAwait(false);
    }
}
