using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Circle.AI.Inference
{
    /// <summary>
    /// Contract for an on-device chat-style text generator. Implementations
    /// own native model state and must be disposed.
    /// </summary>
    public interface IChatGenerator : IDisposable
    {
        /// <summary>
        /// Generates a complete assistant reply for the given conversation.
        /// </summary>
        Task<string> GenerateAsync(
            IReadOnlyList<ChatMessage> messages,
            GenerationOptions? options = null,
            CancellationToken ct = default);

        /// <summary>
        /// Streams the assistant reply token-by-token (or piece-by-piece) as
        /// it is decoded. Each yielded string is the next chunk to append to
        /// the output — callers should concatenate them in order.
        /// </summary>
        IAsyncEnumerable<string> StreamAsync(
            IReadOnlyList<ChatMessage> messages,
            GenerationOptions? options = null,
            CancellationToken ct = default);
    }

    /// <summary>
    /// A single message in a chat history. <see cref="Role"/> is one of
    /// <c>"system"</c>, <c>"user"</c>, or <c>"assistant"</c>.
    /// </summary>
    public sealed record ChatMessage(string Role, string Content);

    /// <summary>
    /// Knobs for a single generation call.
    /// </summary>
    public sealed class GenerationOptions
    {
        /// <summary>Maximum number of new tokens to produce.</summary>
        public int MaxTokens { get; init; } = 512;

        /// <summary>Sampling temperature. 0 = greedy; higher = more random.</summary>
        public float Temperature { get; init; } = 0.7f;

        /// <summary>Nucleus sampling cutoff (top-p). 1.0 disables.</summary>
        public float TopP { get; init; } = 0.9f;

        /// <summary>Top-k cutoff. 0 disables.</summary>
        public int TopK { get; init; } = 40;

        /// <summary>Optional RNG seed. <c>null</c> means non-deterministic.</summary>
        public int? Seed { get; init; }

        /// <summary>
        /// Optional substrings that will end generation when matched in the
        /// emitted output (e.g. role-tag boundaries).
        /// </summary>
        public string[]? StopSequences { get; init; }
    }
}
