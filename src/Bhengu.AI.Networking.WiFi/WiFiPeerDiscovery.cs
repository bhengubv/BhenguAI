using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using Bhengu.AI.Networking;

namespace Bhengu.AI.Networking.WiFi;

/// <summary>
/// Discovers nearby Circle AI devices on the same LAN via UDP broadcast beacons.
/// No Aether, no cloud, no infrastructure required.
/// </summary>
public sealed class WiFiPeerDiscovery : IPeerDiscovery
{
    private const string BeaconMagic = "CIRCLEAI:BEACON:";

    public async IAsyncEnumerable<PeerInfo> DiscoverAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var udp = new UdpClient(WiFiNetworkTransport.DiscoveryPort) { EnableBroadcast = true };
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try { result = await udp.ReceiveAsync(ct).ConfigureAwait(false); }
            catch { yield break; }

            var msg = Encoding.UTF8.GetString(result.Buffer);
            if (msg.StartsWith(BeaconMagic, StringComparison.Ordinal))
            {
                var nodeId = msg[BeaconMagic.Length..];
                yield return new PeerInfo(
                    nodeId,
                    $"WiFi/{result.RemoteEndPoint.Address}",
                    [TransportKind.WiFi],
                    PeerRole.Peer,
                    null,
                    DateTimeOffset.UtcNow);
            }
        }
    }

    public async Task AnnounceAsync(PeerInfo localInfo, CancellationToken ct = default)
    {
        using var udp = new UdpClient { EnableBroadcast = true };
        var beacon = Encoding.UTF8.GetBytes($"{BeaconMagic}{localInfo.NodeId}");
        await udp.SendAsync(beacon, new IPEndPoint(IPAddress.Broadcast, WiFiNetworkTransport.DiscoveryPort), ct)
            .ConfigureAwait(false);
    }
}
