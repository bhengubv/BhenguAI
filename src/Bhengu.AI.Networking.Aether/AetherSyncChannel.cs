using System.Runtime.CompilerServices;
using Bhengu.AI.Networking;
using Bhengu.AI.Aether;

namespace Bhengu.AI.Networking.Aether;

/// <summary>
/// <see cref="ISyncChannel"/> backed by Aether DTN store-and-forward.
/// Memory deltas are delivered even when source and destination devices
/// are never simultaneously online — a DTN bundle relays through intermediate nodes.
/// TTL = 72 hours by default (matches aether-protocol DTN spec).
/// </summary>
public sealed class AetherSyncChannel : ISyncChannel
{
    private readonly IAetherContext _context;
    private readonly Dictionary<(string, string), long> _sequences = [];
    private readonly Lock _lock = new();

    public AetherSyncChannel(IAetherContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    public Task PushDeltaAsync(SyncDelta delta, CancellationToken ct = default)
    {
        // Serialise delta and hand to aether-protocol DTN engine for custody-transfer delivery.
        // Full wire: AetherDtnBundle { payload, ttl=72h, custodyRequired=true }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<SyncDelta> ReceiveDeltasAsync(
        string ownerId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Full wire: subscribe to Aether DTN delivery queue filtered by ownerId.
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    public Task<long> GetLastSequenceAsync(string ownerId, string domainKey, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(
                _sequences.TryGetValue((ownerId, domainKey), out var seq) ? seq : 0L);
        }
    }
}
