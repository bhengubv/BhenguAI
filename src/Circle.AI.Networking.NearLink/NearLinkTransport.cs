using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Circle.AI.Networking;

namespace Circle.AI.Networking.NearLink;

/// <summary>
/// <see cref="INetworkTransport"/> for Huawei SLE / NearLink.
/// NearLink operates at up to 600 m range, 12 Mbps — bridging BLE and WiFi Direct.
/// Full implementation requires the Huawei NearLink SDK (HarmonyOS / Android).
/// This class provides the contract; <see cref="INearLinkAdapter"/> injects platform ops.
/// No Aether required — works standalone on HarmonyOS and compatible Android devices.
/// </summary>
public sealed class NearLinkTransport : INetworkTransport
{
    private readonly INearLinkAdapter _adapter;
    private readonly Channel<NetworkPayload> _inbound = Channel.CreateUnbounded<NetworkPayload>();

    public TransportKind Kind    => TransportKind.NearLink;
    public bool IsAvailable      => _adapter.IsAvailable;

    public NearLinkTransport(INearLinkAdapter adapter)
        => _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));

    public async Task StartAsync(CancellationToken ct = default)
        => await _adapter.StartAsync(_inbound.Writer, ct).ConfigureAwait(false);

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _adapter.StopAsync(ct).ConfigureAwait(false);
        _inbound.Writer.TryComplete();
    }

    public Task SendAsync(NetworkPayload payload, CancellationToken ct = default)
        => _adapter.SendAsync(payload, ct);

    public IAsyncEnumerable<NetworkPayload> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
        => _inbound.Reader.ReadAllAsync(ct);
}

/// <summary>
/// Platform-level NearLink / SLE operations.
/// Implement using the Huawei DevEco NearLink SDK on HarmonyOS,
/// or the NearLink HAL on compatible Android devices.
/// </summary>
public interface INearLinkAdapter
{
    bool IsAvailable { get; }
    Task StartAsync(System.Threading.Channels.ChannelWriter<NetworkPayload> inbound, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task SendAsync(NetworkPayload payload, CancellationToken ct);
}
