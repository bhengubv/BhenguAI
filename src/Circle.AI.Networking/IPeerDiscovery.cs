using System.Runtime.CompilerServices;

namespace Circle.AI.Networking;

/// <summary>
/// Finds nearby devices via mDNS, BLE beacons, NearLink scan, Aether presence, etc.
/// </summary>
public interface IPeerDiscovery
{
    IAsyncEnumerable<PeerInfo> DiscoverAsync(
        [EnumeratorCancellation] CancellationToken ct = default);

    Task AnnounceAsync(PeerInfo localInfo, CancellationToken ct = default);
}
