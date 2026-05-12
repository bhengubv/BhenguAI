// InMemoryPersonaStore.cs

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Circle.AI.Memory
{
    /// <summary>
    /// In-memory <see cref="IPersonaStore"/>. Data is not persisted across
    /// restarts. Suitable for tests and ephemeral CLI sessions.
    /// </summary>
    public sealed class InMemoryPersonaStore : IPersonaStore
    {
        private readonly Dictionary<string, PersonaState> _personas = new();
        private readonly object _lock = new();

        public Task<PersonaState> LoadAsync(string userId, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);
            ct.ThrowIfCancellationRequested();

            lock (_lock)
            {
                if (_personas.TryGetValue(userId, out var existing))
                    return Task.FromResult(existing);

                var fresh = new PersonaState { UserId = userId };
                _personas[userId] = fresh;
                return Task.FromResult(fresh);
            }
        }

        public Task SaveAsync(PersonaState persona, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(persona);
            ct.ThrowIfCancellationRequested();

            persona.LastUpdatedUtc = DateTimeOffset.UtcNow;
            lock (_lock) { _personas[persona.UserId] = persona; }
            return Task.CompletedTask;
        }
    }
}
