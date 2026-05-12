namespace Circle.AI.Identity;

/// <summary>Persistent store for Circle AI identities and device registrations.</summary>
public interface IIdentityStore
{
    Task<CircleIdentity?> GetAsync(string identityId, CancellationToken ct = default);
    Task SaveAsync(CircleIdentity identity, CancellationToken ct = default);
    Task<IReadOnlyList<RegisteredDevice>> GetDevicesAsync(string identityId, CancellationToken ct = default);
    Task RegisterDeviceAsync(RegisteredDevice device, CancellationToken ct = default);
    Task<CircleIdentity?> GetByDeviceAsync(string deviceId, CancellationToken ct = default);
}
