// InMemoryFeedbackStore.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Circle.AI.Memory
{
    /// <summary>
    /// In-memory, thread-safe <see cref="IFeedbackStore"/>.
    /// Data is lost on process exit. For tests and headless CLI use.
    /// </summary>
    public sealed class InMemoryFeedbackStore : IFeedbackStore
    {
        private readonly int _maxSignals;
        private readonly List<FeedbackSignal> _signals = new();
        private readonly object _lock = new();

        public InMemoryFeedbackStore(int maxSignals = 10_000)
        {
            if (maxSignals <= 0) throw new ArgumentOutOfRangeException(nameof(maxSignals));
            _maxSignals = maxSignals;
        }

        public Task AddAsync(FeedbackSignal signal, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(signal);
            ct.ThrowIfCancellationRequested();

            lock (_lock)
            {
                _signals.Add(signal);
                while (_signals.Count > _maxSignals)
                    _signals.RemoveAt(0);
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FeedbackSignal>> GetRecentAsync(
            int count = 50, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_lock)
            {
                IReadOnlyList<FeedbackSignal> r = _signals
                    .OrderByDescending(s => s.RecordedAtUtc)
                    .Take(count)
                    .ToList();
                return Task.FromResult(r);
            }
        }

        public Task<int> CountAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_lock) { return Task.FromResult(_signals.Count); }
        }

        public Task<double?> PositiveRatioAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_lock)
            {
                if (_signals.Count == 0) return Task.FromResult<double?>(null);
                int pos = _signals.Count(s => s.Polarity == FeedbackPolarity.Positive);
                return Task.FromResult<double?>((double)pos / _signals.Count);
            }
        }
    }
}
