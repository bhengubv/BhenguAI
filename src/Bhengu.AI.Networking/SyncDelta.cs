namespace Bhengu.AI.Networking;

/// <summary>
/// An incremental state change that must reach every device owned by
/// <see cref="OwnerId"/>. This is the primitive that makes Circle AI
/// cross-device continuous — HER + JARVIS memory following the person.
/// </summary>
public sealed record SyncDelta(
    string OwnerId,           // identity whose state this belongs to
    string SourceDeviceId,    // origin device
    string TargetDeviceId,    // "" = broadcast to all owned devices
    string DomainKey,         // "memory.episodic" | "affect.state" | "persona" | custom
    ReadOnlyMemory<byte> Payload,
    long Sequence,            // monotonic per owner+domain
    SyncDeliveryMode DeliveryMode,
    TimeSpan? Ttl,
    DateTimeOffset CreatedAt);
