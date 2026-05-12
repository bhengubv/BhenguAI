// InMemoryScheduledTaskStore.cs
//
// Thread-safe, in-process implementation of IScheduledTaskStore.
// Uses ConcurrentDictionary so reads/writes from the polling loop and from
// caller code never race on the dictionary itself.

using System.Collections.Concurrent;

namespace Bhengu.AI.Hosting;

/// <summary>
/// Thread-safe, in-memory implementation of <see cref="IScheduledTaskStore"/>.
/// All state is lost when the process exits. Use a persistent implementation
/// (e.g. SQLite-backed) for durable schedules across restarts.
/// </summary>
public sealed class InMemoryScheduledTaskStore : IScheduledTaskStore
{
    private readonly ConcurrentDictionary<string, CronJob> _store =
        new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task<IReadOnlyList<CronJob>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<CronJob> list = _store.Values.ToList();
        return Task.FromResult(list);
    }

    /// <inheritdoc/>
    public Task<CronJob?> GetAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ct.ThrowIfCancellationRequested();
        _store.TryGetValue(id, out var job);
        return Task.FromResult(job);
    }

    /// <inheritdoc/>
    public Task<CronJob> UpsertAsync(CronJob job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ct.ThrowIfCancellationRequested();
        _store[job.Id] = job;
        return Task.FromResult(job);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ct.ThrowIfCancellationRequested();
        _store.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<CronJob>> GetDueJobsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<CronJob> due = _store.Values
            .Where(j => j.IsEnabled && j.NextRunUtc.HasValue && j.NextRunUtc.Value <= now)
            .ToList();
        return Task.FromResult(due);
    }
}
