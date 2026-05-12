namespace Bhengu.AI.Networking;

/// <summary>
/// Compresses or transforms payloads for low-bandwidth transports
/// (BLE, NearLink, LoRa, DTN).
/// </summary>
public interface IPayloadOptimiser
{
    ValueTask<NetworkPayload> OptimiseAsync(
        NetworkPayload payload,
        TransportKind targetTransport,
        CancellationToken ct = default);

    NetworkPayload Decompress(NetworkPayload payload);
}
