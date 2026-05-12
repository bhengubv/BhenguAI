namespace Circle.AI.Aether;

/// <summary>Kinds of routing changes Aether can emit.</summary>
public enum AetherRouteEventKind
{
    Discovered,
    Changed,
    Failed,
}

/// <summary>
/// Emitted when Aether discovers, updates, or loses a route between two
/// nodes. The path list describes the sequence of node IDs traversed.
/// </summary>
public sealed record AetherRouteEvent(
    string SourceNodeId,
    string DestinationNodeId,
    IReadOnlyList<string> Path,
    AetherRouteEventKind Kind,
    string? FailureReason,
    DateTimeOffset OccurredAt)
{
    /// <summary>Number of hops in this route, including source and destination.</summary>
    public int HopCount => Path.Count;

    /// <summary>True when this event represents a routing failure.</summary>
    public bool IsFailed => Kind is AetherRouteEventKind.Failed;
}
