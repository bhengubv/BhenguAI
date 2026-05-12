namespace Circle.AI.Identity;

/// <summary>
/// Resolves the active identity for the current device/session.
/// Implementations may use local storage, biometrics, or mesh-distributed keys.
/// </summary>
public interface IIdentityProvider
{
    Task<CircleIdentity?> GetCurrentIdentityAsync(CancellationToken ct = default);
    Task<bool> IsAuthenticatedAsync(CancellationToken ct = default);
    Task<CircleIdentity> CreateIdentityAsync(string displayName, string? preferredLanguage = null, CancellationToken ct = default);
}
