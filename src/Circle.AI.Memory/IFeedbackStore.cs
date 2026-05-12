// IFeedbackStore.cs

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Circle.AI.Memory
{
    /// <summary>
    /// Persists user feedback signals for later analysis and on-device
    /// adaptation (LoRA fine-tuning, preference modelling).
    /// </summary>
    public interface IFeedbackStore
    {
        /// <summary>Records a new feedback signal.</summary>
        Task AddAsync(FeedbackSignal signal, CancellationToken ct = default);

        /// <summary>
        /// Returns the most recent <paramref name="count"/> signals,
        /// newest-first.
        /// </summary>
        Task<IReadOnlyList<FeedbackSignal>> GetRecentAsync(
            int count = 50,
            CancellationToken ct = default);

        /// <summary>Total number of signals stored.</summary>
        Task<int> CountAsync(CancellationToken ct = default);

        /// <summary>
        /// Returns the fraction of stored signals that are
        /// <see cref="FeedbackPolarity.Positive"/> (0.0–1.0). Returns
        /// <c>null</c> when no signals are available.
        /// </summary>
        Task<double?> PositiveRatioAsync(CancellationToken ct = default);
    }
}
