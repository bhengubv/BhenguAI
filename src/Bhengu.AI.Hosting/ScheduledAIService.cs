// ScheduledAIService.cs
//
// Background polling service that fires due B! cron jobs.
// Checks the store every 30 seconds, runs each due job via IAIService.AskAsync,
// and emits OnJobCompleted for delivery handling by the host application.

using Microsoft.Extensions.Logging;

namespace Bhengu.AI.Hosting;

/// <summary>Event data emitted when a scheduled job finishes (success or failure).</summary>
/// <param name="Job">The job that was executed (with updated state fields).</param>
/// <param name="Response">The AI response text, or an empty string on failure.</param>
/// <param name="Error">Non-null when execution failed.</param>
public sealed record JobCompletedEventArgs(CronJob Job, string Response, Exception? Error);

/// <summary>
/// Runs a background loop that polls <see cref="IScheduledTaskStore"/> for due
/// <see cref="CronJob"/> records every 30 seconds, executes them via
/// <see cref="IAIService.AskAsync"/>, and raises <see cref="OnJobCompleted"/>.
/// </summary>
/// <remarks>
/// Delivery routing (push, email, Telegram, …) is intentionally left to the host
/// via the <see cref="OnJobCompleted"/> event so that BhenguAI has no dependency
/// on platform-specific notification libraries.
/// </remarks>
public sealed class ScheduledAIService : IAsyncDisposable
{
    private readonly IAIService _butler;
    private readonly IScheduledTaskStore _store;
    private readonly ILogger? _logger;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initialises the service. Call <see cref="StartAsync"/> to begin polling.
    /// </summary>
    /// <param name="butler">The butler service used to process job prompts.</param>
    /// <param name="store">The store that persists cron jobs.</param>
    /// <param name="logger">Optional logger; pass <c>null</c> to silence all output.</param>
    public ScheduledAIService(
        IAIService butler,
        IScheduledTaskStore store,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(butler);
        ArgumentNullException.ThrowIfNull(store);
        _butler = butler;
        _store  = store;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Events
    // ------------------------------------------------------------------

    /// <summary>
    /// Raised on the background polling thread whenever a job completes
    /// (successfully or with an error). Subscribers are responsible for
    /// dispatching the response to the configured <see cref="DeliveryTarget"/>.
    /// </summary>
    public event EventHandler<JobCompletedEventArgs>? OnJobCompleted;

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    /// <summary>
    /// Starts the background polling loop. Calling this when the loop is
    /// already running is a no-op.
    /// </summary>
    public Task StartAsync()
    {
        if (_loopTask is { IsCompleted: false })
            return Task.CompletedTask;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loopTask = Task.Run(() => RunLoopAsync(token), token);
        _logger?.LogInformation("[ScheduledAIService] Started — polling every {Seconds}s.", PollInterval.TotalSeconds);
        return Task.CompletedTask;
    }

    /// <summary>Signals the polling loop to stop and waits for it to exit.</summary>
    public async Task StopAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loopTask is not null)
        {
            try { await _loopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }
        _logger?.LogInformation("[ScheduledAIService] Stopped.");
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
    }

    // ------------------------------------------------------------------
    // Core loop
    // ------------------------------------------------------------------

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessDueJobsAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[ScheduledAIService] Unhandled error in poll cycle.");
            }

            try
            {
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessDueJobsAsync(CancellationToken ct)
    {
        var dueJobs = await _store.GetDueJobsAsync(ct).ConfigureAwait(false);
        if (dueJobs.Count == 0) return;

        _logger?.LogDebug("[ScheduledAIService] Found {Count} due job(s).", dueJobs.Count);

        foreach (var job in dueJobs)
        {
            if (ct.IsCancellationRequested) break;
            await ExecuteJobAsync(job, ct).ConfigureAwait(false);
        }
    }

    private async Task ExecuteJobAsync(CronJob job, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Mark as Running
        var running = job with { State = CronJobState.Running };
        await _store.UpsertAsync(running, ct).ConfigureAwait(false);

        string response = string.Empty;
        Exception? error = null;

        try
        {
            _logger?.LogDebug("[ScheduledAIService] Executing job '{Id}' ({Name}).", job.Id, job.Name);
            response = await _butler.AskAsync(job.Prompt, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancellation is not a job failure — restore previous state and rethrow.
            var restored = job with { State = CronJobState.Pending };
            try { await _store.UpsertAsync(restored, CancellationToken.None).ConfigureAwait(false); }
            catch { /* best-effort */ }
            throw;
        }
        catch (Exception ex)
        {
            error = ex;
            _logger?.LogError(ex, "[ScheduledAIService] Job '{Id}' failed.", job.Id);
        }

        var nextRun = ComputeNextRun(job.CronExpression, now);
        var updatedState = error is null ? CronJobState.Succeeded : CronJobState.Failed;

        var updated = job with
        {
            LastRunUtc  = now,
            NextRunUtc  = nextRun,
            State       = updatedState,
        };

        try
        {
            await _store.UpsertAsync(updated, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[ScheduledAIService] Failed to persist job '{Id}' after execution.", job.Id);
        }

        // Fire event on best-effort basis — subscriber errors must not crash the loop.
        try
        {
            OnJobCompleted?.Invoke(this, new JobCompletedEventArgs(updated, response, error));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[ScheduledAIService] OnJobCompleted subscriber threw for job '{Id}'.", job.Id);
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static DateTimeOffset? ComputeNextRun(string cronExpression, DateTimeOffset after)
    {
        try
        {
            return CronScheduleParser.GetNextOccurrence(cronExpression, after);
        }
        catch
        {
            return null;
        }
    }
}
