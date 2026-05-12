// IEpisodicMemoryStore.cs

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Memory
{
    /// <summary>
    /// Persistent store for episodic memories (conversational exchanges +
    /// embeddings). Implementations may be in-memory (tests/edge), SQLite-vec
    /// (production on-device), or a remote vector database.
    /// </summary>
    public interface IEpisodicMemoryStore
    {
        /// <summary>
        /// Appends a new entry to the store. The store must assign
        /// <see cref="EpisodicMemoryEntry.Id"/> if not already set.
        /// </summary>
        Task AddAsync(EpisodicMemoryEntry entry, CancellationToken ct = default);

        /// <summary>
        /// Returns the <paramref name="topK"/> entries whose embeddings are
        /// most similar (cosine) to <paramref name="queryEmbedding"/>. When
        /// <paramref name="queryEmbedding"/> is <c>null</c>, falls back to
        /// recency (most recent <paramref name="topK"/> entries).
        /// </summary>
        Task<IReadOnlyList<EpisodicMemoryEntry>> SearchAsync(
            float[]? queryEmbedding,
            int topK = 5,
            CancellationToken ct = default);

        /// <summary>
        /// Returns the most recent <paramref name="count"/> entries ordered
        /// newest-first.
        /// </summary>
        Task<IReadOnlyList<EpisodicMemoryEntry>> GetRecentAsync(
            int count = 10,
            CancellationToken ct = default);

        /// <summary>Total number of entries currently stored.</summary>
        Task<int> CountAsync(CancellationToken ct = default);

        /// <summary>
        /// Removes all entries older than <paramref name="cutoff"/>.
        /// Returns the number of entries removed.
        /// </summary>
        Task<int> PruneOlderThanAsync(
            DateTimeOffset cutoff,
            CancellationToken ct = default);
    }
}
