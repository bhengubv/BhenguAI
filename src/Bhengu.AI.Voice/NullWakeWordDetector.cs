namespace Bhengu.AI.Voice;

/// <summary>
/// No-op <see cref="IWakeWordDetector"/> implementation. It tracks listening
/// state but never raises <see cref="IWakeWordDetector.WakeWordDetected"/>.
/// Used as a safe default when no real detector has been wired (e.g. on
/// platforms or builds without a microphone backend).
/// </summary>
public sealed class NullWakeWordDetector : IWakeWordDetector
{
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance with the default Butler / B! wake word "Hey B".
    /// </summary>
    public NullWakeWordDetector() : this("Hey B") { }

    /// <summary>
    /// Initializes a new instance with a custom wake word.
    /// </summary>
    /// <param name="wakeWord">Wake-word phrase to report via <see cref="WakeWord"/>.</param>
    public NullWakeWordDetector(string wakeWord)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wakeWord);
        WakeWord = wakeWord;
    }

    /// <inheritdoc />
    public string WakeWord { get; }

    /// <inheritdoc />
    public bool IsListening { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// This event is declared to satisfy the interface contract but is never
    /// raised by <see cref="NullWakeWordDetector"/>.
    /// </remarks>
#pragma warning disable CS0067 // Event is never used (intentional - null implementation)
    public event EventHandler<WakeWordDetectedEventArgs>? WakeWordDetected;
#pragma warning restore CS0067

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        IsListening = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        IsListening = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }
        _disposed = true;
        IsListening = false;
        return ValueTask.CompletedTask;
    }
}
