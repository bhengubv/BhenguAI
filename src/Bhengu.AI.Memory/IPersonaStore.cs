// IPersonaStore.cs

using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Memory
{
    /// <summary>
    /// Loads and persists <see cref="PersonaState"/> for a specific user.
    /// </summary>
    public interface IPersonaStore
    {
        /// <summary>
        /// Loads the persona for <paramref name="userId"/>. Returns a fresh
        /// default persona when none is found.
        /// </summary>
        Task<PersonaState> LoadAsync(string userId, CancellationToken ct = default);

        /// <summary>
        /// Persists the persona. The implementation must be crash-safe
        /// (write-then-swap or similar) to avoid partial writes.
        /// </summary>
        Task SaveAsync(PersonaState persona, CancellationToken ct = default);
    }
}
