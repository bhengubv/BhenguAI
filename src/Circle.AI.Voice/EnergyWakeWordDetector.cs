namespace Circle.AI.Voice;

/// <summary>
/// <see cref="IWakeWordDetector"/> implementation that combines energy-based
/// VAD with speech-to-text transcription to detect a configurable wake word
/// phrase. Audio is captured continuously via <see cref="IAudioCapture"/>,
/// short speech segments are transcribed, and when the transcription contains
/// the wake word the <see cref="WakeWordDetected"/> event is fired.
/// </summary>
/// <remarks>
/// <para>
/// This is a practical, dependency-light approach to wake-word detection
/// that reuses the existing <see cref="IVoiceTranscriber"/> infrastructure.
/// For production use with very low latency requirements, consider a
/// dedicated keyword-spotting model.
/// </para>
/// <para>
/// The background listening loop runs on the thread pool and can be
/// started/stopped via <see cref="StartAsync"/> / <see cref="StopAsync"/>.
/// </para>
/// </remarks>
public sealed class EnergyWakeWordDetector : IWakeWordDetector
{
    private readonly IAudioCapture _capture;
    private readonly IVoiceTranscriber _transcriber;
    private readonly EnergyVadDetector _vad;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _disposed;

    /// <summary>
    /// Initialise a new energy-based wake-word detector.
    /// </summary>
    /// <param name="capture">
    /// Audio capture source providing PCM 16-bit, 16 kHz mono audio.
    /// </param>
    /// <param name="transcriber">
    /// Voice transcriber used to convert detected speech segments to text.
    /// </param>
    /// <param name="wakeWord">
    /// The phrase to listen for. Matching is case-insensitive and uses
    /// <see cref="string.Contains(string, StringComparison)"/> so that
    /// surrounding words do not prevent detection. Default is <c>"hey b"</c>.
    /// </param>
    /// <param name="energyThreshold">
    /// RMS energy threshold for voice activity detection. See
    /// <see cref="EnergyVadDetector"/> for details.
    /// </param>
    public EnergyWakeWordDetector(
        IAudioCapture capture,
        IVoiceTranscriber transcriber,
        string wakeWord = "hey b",
        float energyThreshold = 0.02f)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(transcriber);
        ArgumentException.ThrowIfNullOrWhiteSpace(wakeWord);

        _capture = capture;
        _transcriber = transcriber;
        WakeWord = wakeWord.Trim();
        _vad = new EnergyVadDetector(energyThreshold, silenceFrames: 10, frameSizeBytes: 640);
    }

    /// <inheritdoc />
    public string WakeWord { get; }

    /// <inheritdoc />
    public bool IsListening { get; private set; }

    /// <inheritdoc />
    public event EventHandler<WakeWordDetectedEventArgs>? WakeWordDetected;

    /// <inheritdoc />
    /// <remarks>
    /// Starts a background loop that captures audio, runs VAD, transcribes
    /// detected speech segments, and checks for the wake word. Idempotent:
    /// calling when already listening has no effect.
    /// </remarks>
    public Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (IsListening) return Task.CompletedTask;

            _cts = new CancellationTokenSource();
            IsListening = true;
            _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Cancels the background listening loop and waits for it to complete.
    /// Idempotent: calling when not listening has no effect.
    /// </remarks>
    public async Task StopAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Task? taskToAwait;

        lock (_gate)
        {
            if (!IsListening) return;

            _cts?.Cancel();
            IsListening = false;
            taskToAwait = _listenTask;
        }

        if (taskToAwait is not null)
        {
            try
            {
                await taskToAwait.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling the listen loop.
            }
        }

        lock (_gate)
        {
            _cts?.Dispose();
            _cts = null;
            _listenTask = null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Swallow — we're disposing.
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Background loop that captures audio, runs VAD, transcribes speech
    /// segments, and fires <see cref="WakeWordDetected"/> when the phrase
    /// is found.
    /// </summary>
    private async Task ListenLoopAsync(CancellationToken ct)
    {
        try
        {
            var audioStream = _capture.CaptureAsync(ct);

            await foreach (var segment in _vad.DetectAsync(audioStream, ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();

                if (!segment.IsSpeech || segment.Audio.Length == 0)
                    continue;

                // Transcribe the speech segment.
                TranscriptionResult result;
                try
                {
                    result = await _transcriber
                        .TranscribeAsync(segment.Audio, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Transcription failed for this segment — skip and keep listening.
                    continue;
                }

                if (string.IsNullOrWhiteSpace(result.Text))
                    continue;

                // Check for wake word (case-insensitive).
                if (result.Text.Contains(WakeWord, StringComparison.OrdinalIgnoreCase))
                {
                    WakeWordDetected?.Invoke(this, new WakeWordDetectedEventArgs
                    {
                        WakeWord = WakeWord,
                        DetectedAt = DateTimeOffset.UtcNow,
                        Confidence = result.Confidence
                    });
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown — swallow.
        }
        finally
        {
            lock (_gate)
            {
                IsListening = false;
            }
        }
    }
}
