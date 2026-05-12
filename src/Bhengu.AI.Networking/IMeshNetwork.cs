namespace Bhengu.AI.Networking;

/// <summary>Mesh-specific: topology, node identity, mesh health.</summary>
public interface IMeshNetwork
{
    string LocalNodeId { get; }
    Task<IReadOnlyList<string>> GetPeerIdsAsync(CancellationToken ct = default);
    Task<NetworkContext> GetMeshHealthAsync(CancellationToken ct = default);
}
