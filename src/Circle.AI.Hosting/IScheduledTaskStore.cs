// IScheduledTaskStore.cs
//
// Persistence contract for B! cron jobs (Track 3).

namespace Circle.AI.Hosting;

/// <summary>
/// Persistence abstraction for <see cref="CronJob"/> records.
/// Implementations may be in-memory, SQLite, or any other backing store.
/// All operations are asynchronous and must be thread-safe.
/// </summary>
public interface IScheduledTaskStore
{
    /// <summary>Returns every registered job, regardless of enabled/disabled state.</summary>
    Task<IReadOnlyList<CronJob>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the job with the given <paramref name="id"/>, or <c>null</c> if not found.
    /// </summary>
    Task<CronJob?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Inserts or replaces the job identified by <see cref="CronJob.Id"/>.
    /// Returns the stored record (identical to the input in the default implementation).
    /// </summary>
    Task<CronJob> UpsertAsync(CronJob job, CancellationToken ct = default);

    /// <summary>
    /// Removes the job with the given <paramref name="id"/>. No-op if it does not exist.
    /// </summary>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Returns all enabled jobs whose <see cref="CronJob.NextRunUtc"/> is in the past
    /// (i.e., <c>&lt;= DateTimeOffset.UtcNow</c>).
    /// </summary>
    Task<IReadOnlyList<CronJob>> GetDueJobsAsync(CancellationToken ct = default);
}
