using System.Runtime.CompilerServices;
using Bhengu.AI.Networking;
using Bhengu.AI.Aether;

namespace Bhengu.AI.Networking.Aether;

/// <summary>
/// <see cref="IPeerDiscovery"/> using Aether presence beacons (Hello/HelloAck).
/// No infrastructure — discovery works over BLE/WiFi Direct/NearLink.
/// </summary>
public sealed class AetherPeerDiscovery : IPeerDiscovery
{
    private readonly IAetherContext _context;

    public AetherPeerDiscovery(IAetherContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    public async IAsyncEnumerable<PeerInfo> DiscoverAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Full wire: subscribe to IAetherTelemetry NodeJoined events
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    public Task AnnounceAsync(PeerInfo localInfo, CancellationToken ct = default)
        => Task.CompletedTask; // Full wire: AetherPresenceBeacon broadcast
}
