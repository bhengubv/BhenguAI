// EpisodicMemoryEntry.cs
//
// A single episodic memory — one conversational exchange (user + assistant)
// plus its pre-computed embedding for vector retrieval.

using System;

namespace Bhengu.AI.Memory
{
    /// <summary>
    /// A single recorded episode (one user↔assistant exchange) stored in
    /// <see cref="IEpisodicMemoryStore"/>.
    /// </summary>
    public sealed class EpisodicMemoryEntry
    {
        /// <summary>Stable identifier for the entry.</summary>
        public Guid Id { get; init; } = Guid.NewGuid();

        /// <summary>UTC timestamp of the assistant's response.</summary>
        public DateTimeOffset RecordedAtUtc { get; init; } = DateTimeOffset.UtcNow;

        /// <summary>The user's message text.</summary>
        public string UserText { get; init; } = string.Empty;

        /// <summary>The assistant's response text.</summary>
        public string AssistantText { get; init; } = string.Empty;

        /// <summary>
        /// Optional identifier for the app context in which the exchange
        /// happened (e.g. <c>"tgn.bidbaas"</c>).
        /// </summary>
        public string? AppContext { get; init; }

        /// <summary>
        /// L2-normalised embedding of <c>UserText + " " + AssistantText</c>,
        /// pre-computed at write time. <c>null</c> if the embedding backend
        /// was unavailable when the entry was stored.
        /// </summary>
        public float[]? Embedding { get; init; }

        /// <summary>
        /// Arbitrary key-value tags (e.g. <c>locale</c>, <c>sentiment</c>).
        /// </summary>
        public System.Collections.Generic.Dictionary<string, string>? Tags { get; init; }
    }
}
