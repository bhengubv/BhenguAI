// FeedbackAnalyser.cs
//
// Analyses a window of FeedbackSignal records and produces PersonaAdaptation
// deltas that AIService applies to PersonaState.
//
// Rules (applied to the most recent N signals, default N=20):
//   - >70% negative signals → VerbosityDelta = -0.1f
//   - >70% positive signals → VerbosityDelta = +0.05f
//   - FormalityDelta is always 0f (reserved for future heuristics)
//   - PreferredTopics is always empty — FeedbackSignal carries no topic tags

using System;
using System.Collections.Generic;
using System.Linq;

namespace Circle.AI.Memory;

/// <summary>
/// Deltas to apply to <see cref="PersonaState"/> after analysing feedback signals.
/// </summary>
public sealed record PersonaAdaptation(
    float VerbosityDelta,
    float FormalityDelta,
    string[] PreferredTopics);

/// <summary>
/// Analyses recent <see cref="FeedbackSignal"/> records and produces
/// <see cref="PersonaAdaptation"/> adjustments.
/// </summary>
public sealed class FeedbackAnalyser
{
    private readonly int _windowSize;

    /// <param name="windowSize">
    /// Number of most-recent signals to consider. Must be at least 1.
    /// </param>
    public FeedbackAnalyser(int windowSize = 20)
    {
        if (windowSize < 1)
            throw new ArgumentOutOfRangeException(
                nameof(windowSize), "Window size must be at least 1.");
        _windowSize = windowSize;
    }

    /// <summary>
    /// Compute persona adaptation from the provided signals.
    /// </summary>
    /// <param name="signals">The full or partial history of feedback signals.</param>
    /// <returns>
    /// A <see cref="PersonaAdaptation"/> whose <c>VerbosityDelta</c> is:
    /// <list type="bullet">
    ///   <item>-0.1f when more than 70 % of the window is negative</item>
    ///   <item>+0.05f when more than 70 % of the window is positive</item>
    ///   <item>0f otherwise</item>
    /// </list>
    /// <c>FormalityDelta</c> is always 0f and <c>PreferredTopics</c> is always
    /// empty because <see cref="FeedbackSignal"/> carries no topic metadata.
    /// </returns>
    public PersonaAdaptation Analyse(IEnumerable<FeedbackSignal> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        var window = signals
            .OrderByDescending(s => s.RecordedAtUtc)
            .Take(_windowSize)
            .ToList();

        if (window.Count == 0)
            return new PersonaAdaptation(0f, 0f, []);

        var positiveCount = window.Count(s => s.Polarity == FeedbackPolarity.Positive);
        var negativeCount = window.Count(s => s.Polarity == FeedbackPolarity.Negative);
        var total         = window.Count;

        float verbosityDelta = 0f;
        var negativeRatio = (float)negativeCount / total;
        var positiveRatio = (float)positiveCount / total;

        if (negativeRatio > 0.70f)
            verbosityDelta = -0.1f;
        else if (positiveRatio > 0.70f)
            verbosityDelta = +0.05f;

        // FeedbackSignal has no Tags property — topics extraction is deferred.
        return new PersonaAdaptation(verbosityDelta, 0f, []);
    }
}
