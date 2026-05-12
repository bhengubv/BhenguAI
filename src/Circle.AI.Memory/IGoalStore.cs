// IGoalStore.cs

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Circle.AI.Memory;

/// <summary>
/// Persists and retrieves <see cref="Goal"/> records for a user.
/// </summary>
public interface IGoalStore
{
    /// <summary>Returns all goals for the given user, in any order.</summary>
    Task<IReadOnlyList<Goal>> ListAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Returns the goal with the given <paramref name="id"/>, or <c>null</c>
    /// if it does not exist.
    /// </summary>
    Task<Goal?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Inserts or replaces the goal. The goal's <c>Id</c> is the natural key.
    /// Returns the stored goal.
    /// </summary>
    Task<Goal> UpsertAsync(Goal goal, CancellationToken ct = default);

    /// <summary>
    /// Deletes the goal with the given <paramref name="id"/>. No-op if not found.
    /// </summary>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Returns all goals for <paramref name="userId"/> where
    /// <see cref="Goal.Status"/> is <see cref="GoalStatus.Active"/>.
    /// </summary>
    Task<IReadOnlyList<Goal>> GetActiveAsync(string userId, CancellationToken ct = default);
}
