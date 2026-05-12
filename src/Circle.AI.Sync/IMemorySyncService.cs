using Circle.AI.Networking;

namespace Circle.AI.Sync;

/// <summary>
/// Pushes and receives memory deltas across all owned devices.
/// The transport is determined by <see cref="ISyncChannel"/> — the app
/// code is identical whether the delta travels gRPC, BLE mesh, or DTN bundle.
/// </summary>
public interface IMemorySyncService
{
    /// <summary>Push a memory delta for <paramref name="ownerId"/> to all other devices.</summary>
    Task PushMemoryDeltaAsync(
        string ownerId, string domainKey, ReadOnlyMemory<byte> delta,
        SyncDeliveryMode mode = SyncDeliveryMode.Guaranteed,
        CancellationToken ct = default);

    /// <summary>Start receiving and applying incoming deltas for <paramref name="ownerId"/>.</summary>
    Task StartReceivingAsync(string ownerId, CancellationToken ct = default);

    /// <summary>Stop receiving.</summary>
    Task StopReceivingAsync(CancellationToken ct = default);
}
