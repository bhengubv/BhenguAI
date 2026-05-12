namespace Bhengu.AI.Networking;

/// <summary>
/// Policy rules applied before choosing a transport.
/// Examples: "WiFi-only", "mesh-first", "no cloud when roaming".
/// </summary>
public interface INetworkPolicy
{
    bool Permits(TransportKind transport, NetworkPayload payload);
    TransportKind? ForceTransport { get; }
    bool MeshFirst { get; }
    bool OfflineQueueEnabled { get; }
    bool AllowCloudTransports { get; }
}
