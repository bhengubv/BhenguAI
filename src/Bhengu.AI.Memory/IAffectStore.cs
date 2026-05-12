// IAffectStore.cs

using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Memory;

/// <summary>
/// Loads and persists <see cref="AffectState"/> for a specific user.
/// </summary>
public interface IAffectStore
{
    /// <summary>
    /// Loads the affect state for <paramref name="userId"/>. Returns a fresh
    /// default state when none is found.
    /// </summary>
    Task<AffectState> LoadAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Persists the affect state. Implementations must be crash-safe
    /// (write-then-swap or similar) to avoid partial writes.
    /// </summary>
    Task SaveAsync(AffectState state, CancellationToken ct = default);
}
