namespace Circle.AI.Voice;

/// <summary>
/// Detects a configured wake word in a continuous audio stream and raises
/// <see cref="WakeWordDetected"/> when the phrase is recognised.
/// Implementations are expected to manage their own audio capture pipeline
/// (microphone open/close) between <see cref="StartAsync"/> and
/// <see cref="StopAsync"/>.
/// </summary>
public interface IWakeWordDetector : IAsyncDisposable
{
    /// <summary>
    /// The phrase the detector listens for (e.g. "Hey B").
    /// </summary>
    string WakeWord { get; }

    /// <summary>
    /// True when the detector is actively listening for the wake word.
    /// </summary>
    bool IsListening { get; }

    /// <summary>
    /// Raised when the wake word is detected with sufficient confidence.
    /// Subscribers should treat the event as the trigger to begin command capture.
    /// </summary>
    event EventHandler<WakeWordDetectedEventArgs>? WakeWordDetected;

    /// <summary>
    /// Begin listening for the wake word. Idempotent: calling when already
    /// listening should complete without error.
    /// </summary>
    /// <param name="ct">Cancellation token used to abort startup.</param>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stop listening and release any audio capture resources held by the
    /// detector. Idempotent: calling when not listening should complete
    /// without error.
    /// </summary>
    /// <param name="ct">Cancellation token used to abort shutdown.</param>
    Task StopAsync(CancellationToken ct = default);
}

/// <summary>
/// Payload describing a single wake-word detection event.
/// </summary>
public sealed class WakeWordDetectedEventArgs : EventArgs
{
    /// <summary>The wake word phrase that was detected.</summary>
    public required string WakeWord { get; init; }

    /// <summary>UTC timestamp at which the detection fired.</summary>
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Detector-reported confidence in the detection, in the range [0, 1].
    /// Implementations that do not produce a confidence score should report 1.0.
    /// </summary>
    public float Confidence { get; init; }
}
