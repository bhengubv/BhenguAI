namespace Circle.AI.Aether;

/// <summary>Physical or logical transport medium Aether is using.</summary>
public enum AetherTransportKind
{
    WiFi,
    Bluetooth,
    LoRa,
    NFC,
    Cellular,
    Ethernet,
    Unknown,
}

/// <summary>Kinds of transport-layer observations Aether can emit.</summary>
public enum AetherTransportEventKind
{
    Selected,
    Changed,
    LatencyMeasured,
    PacketLoss,
}

/// <summary>
/// Emitted when Aether selects, changes, or measures quality on a
/// transport channel. The AI layer uses this to correlate transport
/// behaviour with threat patterns.
/// </summary>
public sealed record AetherTransportEvent(
    string NodeId,
    AetherTransportEventKind Kind,
    AetherTransportKind Transport,
    TimeSpan? Latency,
    double? PacketLossRate,
    DateTimeOffset OccurredAt)
{
    /// <summary>
    /// Returns true when PacketLossRate is set and exceeds the given
    /// threshold (0.0–1.0).
    /// </summary>
    public bool ExceedsLoss(double threshold) =>
        PacketLossRate.HasValue && PacketLossRate.Value > threshold;
}
