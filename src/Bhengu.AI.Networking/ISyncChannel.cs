using System.Runtime.CompilerServices;

namespace Bhengu.AI.Networking;

/// <summary>
/// The cross-device continuity primitive.
/// Pushes memory/state deltas across whatever transport is available:
/// gRPC over 5G, BLE mesh via a neighbour, DTN bundle arriving 6 hours later.
/// App code is identical in every case.
/// This is the primitive that makes Circle AI HER + JARVIS:
/// memory follows the person, not the device.
/// </summary>
public interface ISyncChannel
{
    /// <summary>
    /// Push a delta. Channel selects transport and handles retries.
    /// Returns when accepted (not necessarily delivered for DTN/LocalStore).
    /// </summary>
    Task PushDeltaAsync(SyncDelta delta, CancellationToken ct = default);

    IAsyncEnumerable<SyncDelta> ReceiveDeltasAsync(
        string ownerId,
        [EnumeratorCancellation] CancellationToken ct = default);

    Task<long> GetLastSequenceAsync(
        string ownerId, string domainKey, CancellationToken ct = default);
}
