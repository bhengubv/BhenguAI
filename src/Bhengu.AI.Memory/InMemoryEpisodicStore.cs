// InMemoryEpisodicStore.cs
//
// Thread-safe in-memory implementation of IEpisodicMemoryStore. Used in
// tests and as the default when no persistent backend (SQLite-vec) is
// configured. All data is lost when the process exits.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Memory
{
    /// <summary>
    /// In-memory <see cref="IEpisodicMemoryStore"/>. Thread-safe via a
    /// <c>ReaderWriterLockSlim</c>. Maximum capacity is capped to prevent
    /// unbounded growth on long-running processes.
    /// </summary>
    public sealed class InMemoryEpisodicStore : IEpisodicMemoryStore
    {
        private readonly int _maxEntries;
        private readonly List<EpisodicMemoryEntry> _entries = new();
        private readonly ReaderWriterLockSlim _lock = new();

        /// <param name="maxEntries">
        /// Cap on stored entries. When exceeded the oldest entries are
        /// evicted (FIFO). Default 1000.
        /// </param>
        public InMemoryEpisodicStore(int maxEntries = 1000)
        {
            if (maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));
            _maxEntries = maxEntries;
        }

        /// <inheritdoc />
        public Task AddAsync(EpisodicMemoryEntry entry, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ct.ThrowIfCancellationRequested();

            _lock.EnterWriteLock();
            try
            {
                _entries.Add(entry);
                // Evict oldest when over capacity.
                while (_entries.Count > _maxEntries)
                    _entries.RemoveAt(0);
            }
            finally { _lock.ExitWriteLock(); }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<EpisodicMemoryEntry>> SearchAsync(
            float[]? queryEmbedding,
            int topK = 5,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            _lock.EnterReadLock();
            List<EpisodicMemoryEntry> snapshot;
            try { snapshot = new List<EpisodicMemoryEntry>(_entries); }
            finally { _lock.ExitReadLock(); }

            IReadOnlyList<EpisodicMemoryEntry> result;

            if (queryEmbedding is null || queryEmbedding.Length == 0)
            {
                // No embedding — return most recent.
                result = snapshot
                    .OrderByDescending(e => e.RecordedAtUtc)
                    .Take(topK)
                    .ToList();
            }
            else
            {
                // Cosine similarity search (only against entries that have embeddings
                // with the same dimension as the query).
                result = snapshot
                    .Where(e => e.Embedding is not null &&
                                e.Embedding.Length == queryEmbedding.Length)
                    .Select(e => (Entry: e, Score: CosineSimilarity(queryEmbedding, e.Embedding!)))
                    .OrderByDescending(x => x.Score)
                    .Take(topK)
                    .Select(x => x.Entry)
                    .ToList();
            }

            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<EpisodicMemoryEntry>> GetRecentAsync(
            int count = 10,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            _lock.EnterReadLock();
            List<EpisodicMemoryEntry> snapshot;
            try { snapshot = new List<EpisodicMemoryEntry>(_entries); }
            finally { _lock.ExitReadLock(); }

            IReadOnlyList<EpisodicMemoryEntry> result = snapshot
                .OrderByDescending(e => e.RecordedAtUtc)
                .Take(count)
                .ToList();

            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public Task<int> CountAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _lock.EnterReadLock();
            try { return Task.FromResult(_entries.Count); }
            finally { _lock.ExitReadLock(); }
        }

        /// <inheritdoc />
        public Task<int> PruneOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            _lock.EnterWriteLock();
            try
            {
                int before = _entries.Count;
                _entries.RemoveAll(e => e.RecordedAtUtc < cutoff);
                return Task.FromResult(before - _entries.Count);
            }
            finally { _lock.ExitWriteLock(); }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static float CosineSimilarity(float[] a, float[] b)
        {
            // Both vectors are already L2-normalised (written by TextEmbedder),
            // so cosine similarity == dot product.
            float dot = 0f;
            for (int i = 0; i < a.Length; i++) dot += a[i] * b[i];
            return dot;
        }
    }
}
