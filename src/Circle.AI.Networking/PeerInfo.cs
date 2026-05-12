namespace Circle.AI.Networking;

/// <summary>Describes a discovered peer on any transport.</summary>
public sealed record PeerInfo(
    string NodeId,
    string? DisplayName,
    IReadOnlyList<TransportKind> SupportedTransports,
    PeerRole Role,
    int? SignalStrengthDbm,
    DateTimeOffset LastSeen);
