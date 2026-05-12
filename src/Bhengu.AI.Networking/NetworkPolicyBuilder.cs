namespace Bhengu.AI.Networking;

/// <summary>Fluent builder for <see cref="INetworkPolicy"/>.</summary>
public sealed class NetworkPolicyBuilder
{
    private readonly HashSet<TransportKind> _allowed = [];
    private bool _meshFirst;
    private bool _noCloud;
    private bool _queueEnabled = true;
    private TransportKind? _force;

    public NetworkPolicyBuilder MeshFirst()      { _meshFirst   = true; return this; }
    public NetworkPolicyBuilder NoCloud()        { _noCloud     = true; return this; }
    public NetworkPolicyBuilder DisableQueue()   { _queueEnabled = false; return this; }
    public NetworkPolicyBuilder Force(TransportKind t) { _force = t; return this; }
    public NetworkPolicyBuilder Allow(params TransportKind[] kinds)
    {
        foreach (var k in kinds) _allowed.Add(k);
        return this;
    }

    public INetworkPolicy Build() => new Policy(
        _allowed.Count > 0 ? _allowed.ToHashSet() : null,
        _meshFirst, _noCloud, _queueEnabled, _force);

    private sealed class Policy(
        HashSet<TransportKind>? allowed,
        bool meshFirst,
        bool noCloud,
        bool queueEnabled,
        TransportKind? force) : INetworkPolicy
    {
        public bool Permits(TransportKind t, NetworkPayload _)
        {
            if (noCloud && t is TransportKind.Http or TransportKind.WebSocket
                                or TransportKind.Grpc or TransportKind.Mqtt)
                return false;
            return allowed is null || allowed.Contains(t);
        }
        public TransportKind? ForceTransport   => force;
        public bool MeshFirst                  => meshFirst;
        public bool OfflineQueueEnabled        => queueEnabled;
        public bool AllowCloudTransports       => !noCloud;
    }
}
