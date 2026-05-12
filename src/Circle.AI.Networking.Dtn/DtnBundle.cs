namespace Circle.AI.Networking.Dtn;

/// <summary>A DTN bundle: a self-contained delivery unit with TTL and custody semantics.</summary>
public sealed record DtnBundle(
    string BundleId,
    string SourceNodeId,
    string DestinationNodeId,
    ReadOnlyMemory<byte> Payload,
    DateTimeOffset ExpiresAt,          // default: CreatedAt + 72h
    bool CustodyRequired,              // request custody transfer at each hop
    int HopCount,
    DateTimeOffset CreatedAt);
