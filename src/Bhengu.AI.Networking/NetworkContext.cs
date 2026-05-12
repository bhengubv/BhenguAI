namespace Bhengu.AI.Networking;

/// <summary>Snapshot of current connectivity state.</summary>
public sealed record NetworkContext(
    ConnectivityState State,
    TransportKind PreferredTransport,
    IReadOnlyList<TransportKind> AvailableTransports,
    int? SignalStrengthDbm,
    long? EstimatedBandwidthBps,
    long? LatencyMs,
    int NearbyPeerCount,
    DateTimeOffset SnapshotAt)
{
    public static readonly NetworkContext Offline = new(
        ConnectivityState.Offline,
        TransportKind.LocalStore,
        [],
        null, null, null, 0,
        DateTimeOffset.UtcNow);
}
