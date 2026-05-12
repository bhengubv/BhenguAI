namespace Bhengu.AI.Aether;

/// <summary>Kinds of node lifecycle transitions Aether can emit.</summary>
public enum AetherNodeEventKind
{
    Joined,
    Left,
    HealthChanged,
}

/// <summary>
/// Point-in-time health snapshot for a single mesh node.
/// </summary>
/// <param name="TrustScore">
/// 0.0 (untrusted) to 1.0 (fully trusted). Maintained by the AI Security
/// Layer when active; defaults to 1.0 for all nodes when security layer is
/// off.
/// </param>
public sealed record AetherNodeHealth(
    double TrustScore,
    bool IsReachable,
    TimeSpan Latency,
    int HopCount)
{
    /// <summary>Returns true when TrustScore is within the valid 0–1 range.</summary>
    public bool IsValid => TrustScore is >= 0.0 and <= 1.0;
}

/// <summary>
/// Emitted by Aether whenever a node joins, leaves, or changes health.
/// Consumed by <see cref="IAetherTelemetry"/> subscribers — BhenguAI
/// never writes back into Aether directly.
/// </summary>
public sealed record AetherNodeEvent(
    string NodeId,
    AetherNodeEventKind Kind,
    AetherNodeHealth Health,
    DateTimeOffset OccurredAt)
{
    /// <summary>Convenience: true when this is a departure event.</summary>
    public bool IsExit => Kind is AetherNodeEventKind.Left;
}
