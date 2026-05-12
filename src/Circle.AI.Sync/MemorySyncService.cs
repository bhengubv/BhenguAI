using System.Text.Json;
using Circle.AI.Memory;
using Circle.AI.Networking;

namespace Circle.AI.Sync;

/// <summary>
/// Default <see cref="IMemorySyncService"/> implementation.
/// Serialises memory deltas, routes through <see cref="ISyncChannel"/>,
/// and applies received deltas to the local <see cref="IEpisodicMemoryStore"/>.
/// </summary>
public sealed class MemorySyncService : IMemorySyncService
{
    private readonly ISyncChannel _channel;
    private readonly IEpisodicMemoryStore _store;
    private readonly string _localDeviceId;
    private CancellationTokenSource? _receiveCts;

    public MemorySyncService(
        ISyncChannel channel,
        IEpisodicMemoryStore store,
        string localDeviceId)
    {
        _channel       = channel;
        _store         = store;
        _localDeviceId = localDeviceId;
    }

    public Task PushMemoryDeltaAsync(
        string ownerId, string domainKey, ReadOnlyMemory<byte> delta,
        SyncDeliveryMode mode = SyncDeliveryMode.Guaranteed,
        CancellationToken ct = default)
    {
        var syncDelta = new SyncDelta(
            ownerId,
            _localDeviceId,
            TargetDeviceId: "",        // broadcast to all owned devices
            domainKey,
            delta,
            Sequence: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            mode,
            Ttl: null,
            DateTimeOffset.UtcNow);

        return _channel.PushDeltaAsync(syncDelta, ct);
    }

    public Task StartReceivingAsync(string ownerId, CancellationToken ct = default)
    {
        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = ReceiveLoopAsync(ownerId, _receiveCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopReceivingAsync(CancellationToken ct = default)
    {
        if (_receiveCts is not null)
            await _receiveCts.CancelAsync().ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(string ownerId, CancellationToken ct)
    {
        await foreach (var delta in _channel.ReceiveDeltasAsync(ownerId, ct).ConfigureAwait(false))
        {
            if (delta.SourceDeviceId == _localDeviceId) continue; // skip own echoes

            if (delta.DomainKey == SyncDomainKeys.EpisodicMemory)
            {
                // Full wire: deserialise and upsert into local episodic store
            }
            // Additional domain handlers (affect, persona, goals) go here
        }
    }
}
