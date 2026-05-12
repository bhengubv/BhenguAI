using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Circle.AI.Networking;

namespace Circle.AI.Networking.WiFi;

/// <summary>
/// <see cref="INetworkTransport"/> using LAN UDP broadcast / unicast.
/// No Aether required — works whenever devices share a WiFi network.
/// Discovery uses a UDP broadcast on <see cref="DiscoveryPort"/> (default 47890).
/// </summary>
public sealed class WiFiNetworkTransport : INetworkTransport, IAsyncDisposable
{
    public const int DiscoveryPort = 47890;
    public const int DataPort      = 47891;

    private UdpClient? _sender;
    private UdpClient? _receiver;
    private readonly Channel<NetworkPayload> _inbound = Channel.CreateUnbounded<NetworkPayload>();

    public TransportKind Kind    => TransportKind.WiFi;
    public bool IsAvailable      => _receiver is not null;

    public Task StartAsync(CancellationToken ct = default)
    {
        _sender   = new UdpClient();
        _receiver = new UdpClient(DataPort) { EnableBroadcast = true };
        _ = PumpAsync(ct);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _receiver?.Close();
        _sender?.Close();
        _inbound.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public async Task SendAsync(NetworkPayload payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(_sender);
        var data = payload.Data.ToArray();

        if (payload.DestinationId is { Length: > 0 } dest && IPAddress.TryParse(dest, out var ip))
            await _sender.SendAsync(data, new IPEndPoint(ip, DataPort), ct).ConfigureAwait(false);
        else
        {
            _sender.EnableBroadcast = true;
            await _sender.SendAsync(data, new IPEndPoint(IPAddress.Broadcast, DataPort), ct).ConfigureAwait(false);
        }
    }

    public IAsyncEnumerable<NetworkPayload> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
        => _inbound.Reader.ReadAllAsync(ct);

    private async Task PumpAsync(CancellationToken ct)
    {
        while (_receiver is not null && !ct.IsCancellationRequested)
        {
            try
            {
                var result = await _receiver.ReceiveAsync(ct).ConfigureAwait(false);
                await _inbound.Writer.WriteAsync(
                    NetworkPayload.Create(result.Buffer), ct).ConfigureAwait(false);
            }
            catch { break; }
        }
        _inbound.Writer.TryComplete();
    }

    public ValueTask DisposeAsync() { StopAsync(); return ValueTask.CompletedTask; }
}
