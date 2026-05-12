namespace Circle.AI.Networking;

/// <summary>
/// Immutable envelope for a single message or data unit traversing any transport.
/// Transports must not mutate it — create a new payload instead.
/// </summary>
public sealed record NetworkPayload(
    string Id,
    string? SourceId,
    string? DestinationId,
    ReadOnlyMemory<byte> Data,
    MessagePriority Priority,
    TimeSpan? Ttl,
    string ContentType,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAt)
{
    public static NetworkPayload Create(
        ReadOnlyMemory<byte> data,
        string? destinationId = null,
        MessagePriority priority = MessagePriority.Normal,
        string contentType = "application/octet-stream",
        TimeSpan? ttl = null)
    => new(
        Guid.NewGuid().ToString("N"),
        null,
        destinationId,
        data,
        priority,
        ttl,
        contentType,
        new Dictionary<string, string>(),
        DateTimeOffset.UtcNow);
}
