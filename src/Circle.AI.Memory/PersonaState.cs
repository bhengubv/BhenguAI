// PersonaState.cs
//
// Mutable snapshot of B!'s evolving persona for a specific user. Updated
// after each session based on feedback signals, detected preferences, and
// interaction patterns. Persisted by IPersonaStore.
//
// This is the "HER" layer — Samantha changed because she tracked what
// resonated. PersonaState is the data structure behind that evolution.

using System;
using System.Collections.Generic;

namespace Circle.AI.Memory
{
    /// <summary>
    /// B!'s dynamic persona state for a specific user. Persisted between
    /// sessions and injected into the system prompt to shape tone, vocabulary,
    /// and topical depth.
    /// </summary>
    public sealed class PersonaState
    {
        // ------------------------------------------------------------------
        // Identity
        // ------------------------------------------------------------------

        /// <summary>
        /// Opaque user identifier (device ID or hashed phone number).
        /// Never contains PII in plaintext.
        /// </summary>
        public string UserId { get; init; } = "default";

        /// <summary>UTC time of the last update to this persona.</summary>
        public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

        // ------------------------------------------------------------------
        // Communication style
        // ------------------------------------------------------------------

        /// <summary>
        /// Preferred response verbosity inferred from feedback:
        /// <c>"brief"</c>, <c>"balanced"</c> (default), or <c>"detailed"</c>.
        /// </summary>
        public string Verbosity { get; set; } = "balanced";

        /// <summary>
        /// Formality level inferred from the user's own language:
        /// <c>"casual"</c>, <c>"neutral"</c> (default), or <c>"formal"</c>.
        /// </summary>
        public string Formality { get; set; } = "neutral";

        /// <summary>
        /// Preferred response language/locale (IETF BCP-47).
        /// <c>null</c> means "match the device locale".
        /// </summary>
        public string? PreferredLocale { get; set; }

        // ------------------------------------------------------------------
        // Interest signals
        // ------------------------------------------------------------------

        /// <summary>
        /// Weighted topic interests accumulated from positive interactions.
        /// Key = normalised topic label (e.g. <c>"finance"</c>, <c>"sport"</c>),
        /// Value = accumulated positive-signal weight (unbounded positive float).
        /// </summary>
        public Dictionary<string, float> TopicWeights { get; init; } = new();

        /// <summary>
        /// Topics the user has down-voted or explicitly rejected.
        /// </summary>
        public HashSet<string> DisfavouredTopics { get; init; } = new();

        // ------------------------------------------------------------------
        // Interaction stats
        // ------------------------------------------------------------------

        /// <summary>Total number of recorded interactions with this persona.</summary>
        public int TotalInteractions { get; set; }

        /// <summary>Cumulative positive feedback signals.</summary>
        public int PositiveSignals { get; set; }

        /// <summary>Cumulative negative feedback signals.</summary>
        public int NegativeSignals { get; set; }

        /// <summary>
        /// Derived satisfaction score 0.0–1.0. Returns <c>null</c> when
        /// insufficient data (fewer than 10 signals).
        /// </summary>
        public double? SatisfactionScore =>
            (PositiveSignals + NegativeSignals) < 10
                ? null
                : (double)PositiveSignals / (PositiveSignals + NegativeSignals);

        // ------------------------------------------------------------------
        // System-prompt injection
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds a compact persona instruction block suitable for prepending
        /// to the B! system prompt. Returns an empty string when the persona
        /// is in its default/unlearned state.
        /// </summary>
        public string ToSystemPromptHint()
        {
            var hints = new List<string>();

            if (Verbosity != "balanced")
                hints.Add($"Keep responses {Verbosity}.");

            if (Formality == "casual")
                hints.Add("Use a casual, friendly tone.");
            else if (Formality == "formal")
                hints.Add("Maintain a formal, professional tone.");

            if (!string.IsNullOrWhiteSpace(PreferredLocale))
                hints.Add($"Respond in the language appropriate for locale {PreferredLocale}.");

            if (hints.Count == 0) return string.Empty;

            return "[User preferences]\n" + string.Join("\n", hints) + "\n";
        }
    }
}
