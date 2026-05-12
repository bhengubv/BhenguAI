using Circle.AI.Companion;
using Circle.AI.Hosting;

namespace Circle.AI.Ambient;

/// <summary>
/// Always-on background monitor. Periodically evaluates proactive triggers
/// via <see cref="IProactiveReasoningService"/> and surfaces any generated
/// messages to the host (smart speaker, room display, car screen).
/// Designed for ultra-low CPU budgets between trigger checks.
/// </summary>
public sealed class AmbientCompanionMonitor : IAsyncDisposable
{
    private readonly ICompanionSession            _session;
    private readonly IProactiveReasoningService?  _proactive;
    private readonly TimeSpan                     _pollInterval;
    private CancellationTokenSource?              _cts;
    private bool _disposed;

    /// <summary>
    /// Raised when the Companion has a proactive message to surface on the
    /// ambient display or speaker.
    /// </summary>
    public event EventHandler<CompanionProactiveEvent>? ProactiveMessageReady;

    public AmbientCompanionMonitor(
        ICompanionSession session,
        IProactiveReasoningService? proactive = null,
        TimeSpan? pollInterval = null)
    {
        _session      = session ?? throw new ArgumentNullException(nameof(session));
        _proactive    = proactive;
        _pollInterval = pollInterval ?? TimeSpan.FromMinutes(5);

        _session.ProactiveMessageReady += (s, e) => ProactiveMessageReady?.Invoke(this, e);
    }

    /// <summary>
    /// Starts the background poll loop. Non-blocking — returns immediately.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cts is not null) return; // Already running.

        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    /// <summary>Stops the background poll loop.</summary>
    public void Stop() => _cts?.Cancel();

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, ct).ConfigureAwait(false);

                if (_proactive is not null)
                    await _proactive.CheckAsync(_session.IdentityId, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch { /* Swallow — ambient monitor must never crash the host process. */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        await _session.DisposeAsync().ConfigureAwait(false);
    }
}
