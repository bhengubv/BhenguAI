// InMemoryGoalStore.cs
//
// Thread-safe in-memory implementation of IGoalStore backed by a
// ConcurrentDictionary. Intended for testing and single-session scenarios.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Circle.AI.Memory;

/// <summary>
/// Thread-safe in-memory <see cref="IGoalStore"/>.
/// All data is lost when the process exits. Use <see cref="SqliteGoalStore"/>
/// for durable persistence.
/// </summary>
public sealed class InMemoryGoalStore : IGoalStore
{
    private readonly ConcurrentDictionary<string, Goal> _goals = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<IReadOnlyList<Goal>> ListAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ct.ThrowIfCancellationRequested();

        var list = _goals.Values
            .Where(g => string.Equals(g.UserId, userId, StringComparison.Ordinal))
            .ToList();

        return Task.FromResult<IReadOnlyList<Goal>>(list);
    }

    /// <inheritdoc />
    public Task<Goal?> GetAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ct.ThrowIfCancellationRequested();

        _goals.TryGetValue(id, out var goal);
        return Task.FromResult<Goal?>(goal);
    }

    /// <inheritdoc />
    public Task<Goal> UpsertAsync(Goal goal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ct.ThrowIfCancellationRequested();

        _goals[goal.Id] = goal;
        return Task.FromResult(goal);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ct.ThrowIfCancellationRequested();

        _goals.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Goal>> GetActiveAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ct.ThrowIfCancellationRequested();

        var active = _goals.Values
            .Where(g => string.Equals(g.UserId, userId, StringComparison.Ordinal)
                     && g.Status == GoalStatus.Active)
            .ToList();

        return Task.FromResult<IReadOnlyList<Goal>>(active);
    }
}
