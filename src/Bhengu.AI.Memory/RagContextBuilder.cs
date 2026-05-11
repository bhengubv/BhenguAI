// RagContextBuilder.cs
//
// Builds a retrieval-augmented context string from the episodic store.
// Injected into the system prompt so B! can reference past exchanges
// without the full conversation history eating the context window.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bhengu.AI.Embeddings;

namespace Bhengu.AI.Memory
{
    /// <summary>
    /// Retrieves the most semantically relevant episodes from
    /// <see cref="IEpisodicMemoryStore"/> and formats them as a compact
    /// context block for injection into the B! system prompt.
    /// </summary>
    public sealed class RagContextBuilder
    {
        private readonly IEpisodicMemoryStore _store;
        private readonly ITextEmbedder? _embedder;
        private readonly int _topK;
        private readonly int _maxCharsPerEntry;

        /// <param name="store">The episodic store to query.</param>
        /// <param name="embedder">
        /// Optional embedder. When provided, uses semantic similarity to rank
        /// results. When <c>null</c>, falls back to recency ranking.
        /// </param>
        /// <param name="topK">Maximum number of episodes to include. Default 5.</param>
        /// <param name="maxCharsPerEntry">
        /// Maximum characters taken from each episode's texts. Keeps the
        /// injected context from dominating the context window. Default 300.
        /// </param>
        public RagContextBuilder(
            IEpisodicMemoryStore store,
            ITextEmbedder? embedder = null,
            int topK = 5,
            int maxCharsPerEntry = 300)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _embedder = embedder;
            _topK = Math.Max(1, topK);
            _maxCharsPerEntry = Math.Max(50, maxCharsPerEntry);
        }

        /// <summary>
        /// Builds a context block for the given <paramref name="query"/> text.
        /// Returns an empty string when the store is empty or all retrievals
        /// fail (RAG is best-effort and must never block inference).
        /// </summary>
        public async Task<string> BuildContextAsync(
            string query,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query)) return string.Empty;

            try
            {
                float[]? queryEmbedding = null;
                if (_embedder is not null)
                {
                    try
                    {
                        queryEmbedding = await _embedder
                            .GenerateAsync(query, ct)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // Embedding failure is non-fatal — fall back to recency.
                    }
                }

                var entries = await _store
                    .SearchAsync(queryEmbedding, _topK, ct)
                    .ConfigureAwait(false);

                if (entries.Count == 0) return string.Empty;

                return FormatEntries(entries);
            }
            catch
            {
                // RAG is strictly best-effort — never break inference.
                return string.Empty;
            }
        }

        // ------------------------------------------------------------------
        // Formatting
        // ------------------------------------------------------------------

        private string FormatEntries(IReadOnlyList<EpisodicMemoryEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[Relevant past exchanges — for context only]");

            foreach (var e in entries)
            {
                var user = Truncate(e.UserText, _maxCharsPerEntry / 2);
                var asst = Truncate(e.AssistantText, _maxCharsPerEntry / 2);
                var when = e.RecordedAtUtc.ToString("yyyy-MM-dd HH:mm") + " UTC";

                sb.Append("• [").Append(when).Append("] ");
                if (!string.IsNullOrWhiteSpace(e.AppContext))
                    sb.Append('(').Append(e.AppContext).Append(") ");
                sb.Append("User: ").AppendLine(user);
                sb.Append("  B!: ").AppendLine(asst);
            }

            return sb.ToString();
        }

        private static string Truncate(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length <= maxLen) return text;
            return text[..(maxLen - 1)] + "…";
        }
    }
}
