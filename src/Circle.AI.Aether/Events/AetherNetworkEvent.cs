namespace Circle.AI.Aether;

/// <summary>Mesh-wide topology and congestion observations.</summary>
public enum AetherNetworkEventKind
{
    TopologyChanged,
    CongestionDetected,
    PartitionDetected,
}

/// <summary>
/// Emitted when the mesh topology or overall network health changes.
/// Provides aggregate context that the AI layer uses alongside individual
/// node events.
/// </summary>
public sealed record AetherNetworkEvent(
    AetherNetworkEventKind Kind,
    int NodeCount,
    int ActiveRouteCount,
    double CongestionLevel,
    DateTimeOffset OccurredAt)
{
    /// <summary>
    /// True when CongestionLevel exceeds 0.75 — a useful default alert
    /// threshold. Callers may apply their own thresholds.
    /// </summary>
    public bool IsHighCongestion => CongestionLevel > 0.75;
}
