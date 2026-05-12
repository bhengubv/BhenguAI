#nullable enable

using System;

namespace Circle.AI.Inference;

/// <summary>
/// Tracks token usage against a fixed context window and signals when the KV
/// cache should be partially evicted to keep inference latency manageable.
/// </summary>
public sealed class ContextWindowBudgetManager
{
    /// <summary>Maximum number of tokens the model's context window can hold.</summary>
    public int ContextSize { get; }

    /// <summary>Cumulative tokens consumed so far (prompt + completion).</summary>
    public int UsedTokens { get; private set; }

    /// <summary>Tokens still available before the context window is full.</summary>
    public int RemainingTokens => ContextSize - UsedTokens;

    /// <summary>Proportion of the context window that is currently occupied (0–1).</summary>
    public double FillRatio => (double)UsedTokens / ContextSize;

    /// <summary>
    /// Fill ratio at or above which <see cref="ShouldEvict"/> becomes <see langword="true"/>.
    /// </summary>
    public double EvictionThreshold { get; }

    /// <summary>
    /// <see langword="true"/> when the fill ratio has reached or exceeded
    /// <see cref="EvictionThreshold"/> and older context should be dropped.
    /// </summary>
    public bool ShouldEvict => FillRatio >= EvictionThreshold;

    /// <summary>
    /// Initialises a new budget manager.
    /// </summary>
    /// <param name="contextSize">Total context window size in tokens. Must be &gt; 0.</param>
    /// <param name="evictionThreshold">
    /// Fill ratio (0–1) that triggers eviction. Defaults to 0.85 (85 %).
    /// </param>
    public ContextWindowBudgetManager(int contextSize, double evictionThreshold = 0.85)
    {
        if (contextSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(contextSize),
                "Context size must be greater than zero.");

        if (evictionThreshold is < 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(evictionThreshold),
                "Eviction threshold must be in the range [0, 1].");

        ContextSize = contextSize;
        EvictionThreshold = evictionThreshold;
    }

    /// <summary>
    /// Records the token cost of one exchange (a prompt + its completion).
    /// </summary>
    /// <param name="promptTokens">Number of tokens in the prompt.</param>
    /// <param name="completionTokens">Number of tokens in the model's reply.</param>
    public void RecordExchange(int promptTokens, int completionTokens)
    {
        if (promptTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(promptTokens),
                "Token counts must not be negative.");
        if (completionTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(completionTokens),
                "Token counts must not be negative.");

        UsedTokens += promptTokens + completionTokens;
    }

    /// <summary>
    /// Calculates how many of the oldest tokens should be dropped so that
    /// <see cref="FillRatio"/> returns to <paramref name="targetFillRatio"/>.
    /// Returns 0 when the fill ratio is already at or below the target.
    /// </summary>
    /// <param name="targetFillRatio">Desired fill ratio after eviction. Defaults to 0.50.</param>
    public int CalculateEvictionCount(double targetFillRatio = 0.50)
    {
        if (targetFillRatio is < 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(targetFillRatio),
                "Target fill ratio must be in the range [0, 1].");

        var targetUsed = (int)(ContextSize * targetFillRatio);
        var evict = UsedTokens - targetUsed;
        return evict > 0 ? evict : 0;
    }

    /// <summary>
    /// Resets the used-token counter to zero. Call this after clearing the KV cache.
    /// </summary>
    public void Reset() => UsedTokens = 0;
}
