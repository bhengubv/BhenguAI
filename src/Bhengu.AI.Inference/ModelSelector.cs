#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Bhengu.AI.Inference;

/// <summary>
/// Describes a quantization tier: a model variant and its minimum RAM requirement.
/// </summary>
/// <param name="ModelId">Logical model identifier (used with <see cref="IModelDownloadService"/>).</param>
/// <param name="MinRamBytes">Minimum available RAM in bytes required to run this tier comfortably.</param>
/// <param name="Description">Human-readable description of the target device class.</param>
public sealed record ModelTier(string ModelId, long MinRamBytes, string Description);

/// <summary>
/// Selects the highest-quality model tier that fits within the available RAM.
/// </summary>
public static class ModelSelector
{
    private static readonly IReadOnlyList<ModelTier> _defaultTiers =
        new List<ModelTier>
        {
            new("Qwen3-1.7B-Q4",       MinRamBytes: 2L  * 1024 * 1024 * 1024, "Low-end phone / wearable"),
            new("Qwen3-4B-Q4",         MinRamBytes: 4L  * 1024 * 1024 * 1024, "Mid-range phone"),
            new("Qwen3.6-35B-A3B-Q3",  MinRamBytes: 8L  * 1024 * 1024 * 1024, "Flagship phone / tablet (default)"),
            new("Qwen3-30B-A3B-Q4",    MinRamBytes: 16L * 1024 * 1024 * 1024, "Desktop / laptop"),
            new("Qwen3-235B-A22B-Q2",  MinRamBytes: 48L * 1024 * 1024 * 1024, "Server / workstation"),
        }.AsReadOnly();

    /// <summary>
    /// The built-in B! model lineup, ordered from lowest to highest RAM requirement.
    /// </summary>
    public static IReadOnlyList<ModelTier> DefaultTiers => _defaultTiers;

    /// <summary>
    /// Selects the highest tier whose <see cref="ModelTier.MinRamBytes"/> does not exceed
    /// <paramref name="availableRamBytes"/>. Falls back to the lowest tier when no tier fits.
    /// </summary>
    /// <param name="availableRamBytes">Available RAM on the device in bytes.</param>
    /// <param name="tiers">
    /// Optional custom tier list. When <see langword="null"/>, <see cref="DefaultTiers"/> is used.
    /// </param>
    public static ModelTier Select(long availableRamBytes, IEnumerable<ModelTier>? tiers = null)
    {
        var tierList = (tiers ?? DefaultTiers)
            .OrderBy(t => t.MinRamBytes)
            .ToList();

        if (tierList.Count == 0)
            throw new ArgumentException("Tier list must contain at least one entry.", nameof(tiers));

        // Walk from highest to lowest; return the first that fits.
        ModelTier? best = null;
        foreach (var tier in tierList)
        {
            if (tier.MinRamBytes <= availableRamBytes)
                best = tier;
        }

        // Fall back to the lowest tier when nothing fits (better than nothing).
        return best ?? tierList[0];
    }

    /// <summary>
    /// Convenience overload that reads available RAM from the GC memory info.
    /// </summary>
    /// <param name="tiers">Optional custom tier list.</param>
    public static ModelTier SelectForCurrentDevice(IEnumerable<ModelTier>? tiers = null)
    {
        var info = GC.GetGCMemoryInfo();
        return Select(info.TotalAvailableMemoryBytes, tiers);
    }
}
