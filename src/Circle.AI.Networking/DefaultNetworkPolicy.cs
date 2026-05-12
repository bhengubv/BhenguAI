namespace Circle.AI.Networking;

/// <summary>Permissive default: all transports allowed, offline queue on.</summary>
public sealed class DefaultNetworkPolicy : INetworkPolicy
{
    public static readonly DefaultNetworkPolicy Instance = new();
    private DefaultNetworkPolicy() { }

    public bool Permits(TransportKind transport, NetworkPayload payload) => true;
    public TransportKind? ForceTransport => null;
    public bool MeshFirst => false;
    public bool OfflineQueueEnabled => true;
    public bool AllowCloudTransports => true;
}
