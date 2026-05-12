namespace Bhengu.AI.Identity;

public enum IdentityTier { Anonymous, Pseudonymous, Verified }

/// <summary>
/// A Circle AI identity — the unified persona key that travels with the person.
/// Phone → Watch → Desktop → Smart Speaker → Car: same identity, same memory.
/// </summary>
public sealed record CircleIdentity(
    string IdentityId,        // stable GUID — never changes
    string DisplayName,
    string? PreferredLanguage,
    IdentityTier Tier,
    IReadOnlyList<string> DeviceIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt);

/// <summary>A device registered to an identity.</summary>
public sealed record RegisteredDevice(
    string DeviceId,
    string IdentityId,
    string Platform,          // "android" | "ios" | "windows" | "macos" | "linux" | "web" | "watch" | "iot"
    string? DeviceName,
    DateTimeOffset RegisteredAt,
    DateTimeOffset LastActiveAt);
