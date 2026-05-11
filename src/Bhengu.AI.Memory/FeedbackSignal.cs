// FeedbackSignal.cs
//
// Captures a user's reaction to a B! response. Accumulated signals form
// the dataset for eventual on-device LoRA fine-tuning (v2.1).

using System;

namespace Bhengu.AI.Memory
{
    /// <summary>Polarity of the feedback signal.</summary>
    public enum FeedbackPolarity
    {
        /// <summary>User explicitly approved / up-voted the response.</summary>
        Positive = 1,

        /// <summary>User explicitly rejected / down-voted the response.</summary>
        Negative = -1,

        /// <summary>
        /// User provided a correction (neutral polarity, but carries the
        /// preferred text in <see cref="FeedbackSignal.CorrectedText"/>).
        /// </summary>
        Correction = 0,
    }

    /// <summary>
    /// A single user-feedback event tied to a specific B! response.
    /// Stored by <see cref="IFeedbackStore"/> for later analysis and
    /// potential on-device adaptation.
    /// </summary>
    public sealed class FeedbackSignal
    {
        /// <summary>Stable identifier for the signal.</summary>
        public Guid Id { get; init; } = Guid.NewGuid();

        /// <summary>UTC time when the user provided the signal.</summary>
        public DateTimeOffset RecordedAtUtc { get; init; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// The <see cref="EpisodicMemoryEntry.Id"/> of the episode this
        /// feedback refers to, if the exchange was also stored episodically.
        /// </summary>
        public Guid? EpisodeId { get; init; }

        /// <summary>The user's original message.</summary>
        public string UserText { get; init; } = string.Empty;

        /// <summary>B!'s response that is being rated.</summary>
        public string AssistantText { get; init; } = string.Empty;

        /// <summary>User's rating.</summary>
        public FeedbackPolarity Polarity { get; init; }

        /// <summary>
        /// For <see cref="FeedbackPolarity.Correction"/> signals — the user's
        /// preferred response that should have been given.
        /// </summary>
        public string? CorrectedText { get; init; }

        /// <summary>
        /// Free-text comment the user optionally attached to the signal.
        /// </summary>
        public string? Comment { get; init; }
    }
}
