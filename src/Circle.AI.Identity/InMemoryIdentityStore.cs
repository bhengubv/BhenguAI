namespace Circle.AI.Identity;

/// <summary>In-memory identity store for development and testing.</summary>
public sealed class InMemoryIdentityStore : IIdentityStore
{
    private readonly Dictionary<string, CircleIdentity>    _identities = [];
    private readonly Dictionary<string, RegisteredDevice>  _devices    = [];
    private readonly Lock _lock = new();

    public Task<CircleIdentity?> GetAsync(string identityId, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_identities.GetValueOrDefault(identityId));
    }

    public Task SaveAsync(CircleIdentity identity, CancellationToken ct = default)
    {
        lock (_lock) _identities[identity.IdentityId] = identity;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RegisteredDevice>> GetDevicesAsync(string identityId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<RegisteredDevice> result =
                _devices.Values.Where(d => d.IdentityId == identityId).ToList();
            return Task.FromResult(result);
        }
    }

    public Task RegisterDeviceAsync(RegisteredDevice device, CancellationToken ct = default)
    {
        lock (_lock) _devices[device.DeviceId] = device;
        return Task.CompletedTask;
    }

    public Task<CircleIdentity?> GetByDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var device = _devices.GetValueOrDefault(deviceId);
            if (device is null) return Task.FromResult<CircleIdentity?>(null);
            return Task.FromResult(_identities.GetValueOrDefault(device.IdentityId));
        }
    }
}
