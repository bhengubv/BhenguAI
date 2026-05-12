namespace Bhengu.AI.Networking;

/// <summary>
/// Selects the best transport for a payload+context.
/// Default cascade: gRPC → WebSocket → HTTP → MQTT → TCP →
///   WiFi → Bluetooth → NearLink → Aether → DTN → LocalStore
/// </summary>
public interface ITransportSelector
{
    TransportKind SelectBest(NetworkPayload payload, NetworkContext context);
    IReadOnlyList<TransportKind> GetCascade(NetworkPayload payload, NetworkContext context);
}
