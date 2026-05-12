using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Bhengu.AI.Networking;

namespace Bhengu.AI.Networking.Bluetooth;

/// <summary>
/// Stub <see cref="INetworkTransport"/> for BLE GATT.
/// Full implementation requires platform-specific BLE APIs
/// (Windows.Devices.Bluetooth on Windows, CoreBluetooth on iOS/macOS,
///  BluetoothGatt on Android via MAUI).
/// This base provides the contract; platform adapters inject the real GATT operations.
/// </summary>
public sealed class BluetoothNetworkTransport : INetworkTransport
{
    private readonly IBleGattAdapter _adapter;
    private readonly Channel<NetworkPayload> _inbound = Channel.CreateUnbounded<NetworkPayload>();

    public TransportKind Kind    => TransportKind.Bluetooth;
    public bool IsAvailable      => _adapter.IsAvailable;

    public BluetoothNetworkTransport(IBleGattAdapter adapter)
        => _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));

    public async Task StartAsync(CancellationToken ct = default)
        => await _adapter.StartAsync(_inbound.Writer, ct).ConfigureAwait(false);

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _adapter.StopAsync(ct).ConfigureAwait(false);
        _inbound.Writer.TryComplete();
    }

    public Task SendAsync(NetworkPayload payload, CancellationToken ct = default)
        => _adapter.WriteAsync(payload, ct);

    public IAsyncEnumerable<NetworkPayload> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
        => _inbound.Reader.ReadAllAsync(ct);
}

/// <summary>Platform-specific BLE GATT operations. Implement per platform (MAUI, Windows, Linux).</summary>
public interface IBleGattAdapter
{
    bool IsAvailable { get; }
    Task StartAsync(System.Threading.Channels.ChannelWriter<NetworkPayload> inbound, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task WriteAsync(NetworkPayload payload, CancellationToken ct);
}
