namespace Bhengu.AI.Security;

using System.Collections.Concurrent;
using System.Threading.Channels;

// ─────────────────────────────────────────────────────────────────────────────
// Thread-safe, per-peer trust store.
//
// - Each peer gets a score in [0, 1]. 1.0 = fully trusted; 0.0 = fully lost.
// - ApplyDegradation drops the score and records the triggering event.
// - ApplyRecovery heals all peers passively (called by a background timer).
// - TrustScoreUpdates is an unbounded channel; readers receive every change.
//
// Transport-agnostic: stores PeerSecurityEvent, emits PeerTrustScoreUpdate.
// No dependency on any transport package.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Per-peer mutable trust state. Exposed for diagnostics and tests.</summary>
public sealed class NodeTrustEntry
{
    public required string NodeId          { get; init; }
    public double TrustScore               { get; internal set; }
    public DateTimeOffset LastUpdated      { get; internal set; } = DateTimeOffset.UtcNow;

    /// <summary>Bounded history of security events (oldest-first).</summary>
    public List<PeerSecurityEvent> RecentEvents { get; } = new();
}

/// <summary>
/// Maintains per-peer trust scores, event history, and a live channel of
/// trust score changes consumed by <see cref="PeerIntelligenceService"/>.
/// </summary>
public sealed class NodeTrustRegistry
{
    private readonly SecurityOptions _options;
    private readonly ConcurrentDictionary<string, NodeTrustEntry> _nodes = new();

    // Single writer / multiple readers — matches one background recovery loop
    // writing and multiple intelligence subscribers reading.
    private readonly Channel<PeerTrustScoreUpdate> _channel =
        Channel.CreateUnbounded<PeerTrustScoreUpdate>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    public NodeTrustRegistry(SecurityOptions options) => _options = options;

    /// <summary>
    /// Stream of trust score changes; never completes during normal operation.
    /// Callers should pass a <see cref="CancellationToken"/> to break out.
    /// </summary>
    public ChannelReader<PeerTrustScoreUpdate> TrustScoreUpdates => _channel.Reader;

    // ─── Peer access ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the existing entry for <paramref name="nodeId"/>, or creates
    /// a new one initialised to <see cref="SecurityOptions.InitialTrustScore"/>.
    /// </summary>
    public NodeTrustEntry GetOrCreate(string nodeId) =>
        _nodes.GetOrAdd(nodeId, id => new NodeTrustEntry
        {
            NodeId     = id,
            TrustScore = _options.InitialTrustScore,
        });

    /// <summary>All peer IDs currently tracked.</summary>
    public IEnumerable<string> AllNodeIds => _nodes.Keys;

    /// <summary>
    /// Returns the current trust score for <paramref name="nodeId"/>,
    /// or <see cref="SecurityOptions.InitialTrustScore"/> for unknown peers.
    /// </summary>
    public double GetTrustScore(string nodeId)
    {
        if (_nodes.TryGetValue(nodeId, out var entry))
            lock (entry) return entry.TrustScore;

        return _options.InitialTrustScore;
    }

    // ─── Mutations ────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies trust degradation for a security event.
    /// Score is clamped to [0, 1]; the event is appended to the per-peer
    /// history; a <see cref="PeerTrustScoreUpdate"/> is published on the channel.
    /// Returns <c>(previousScore, newScore)</c>.
    /// </summary>
    public (double Previous, double Current) ApplyDegradation(
        PeerSecurityEvent securityEvent, double degradationAmount)
    {
        var entry = GetOrCreate(securityEvent.NodeId);

        lock (entry)
        {
            var previous = entry.TrustScore;
            entry.TrustScore  = Math.Clamp(previous - degradationAmount, 0.0, 1.0);
            entry.LastUpdated = securityEvent.OccurredAt;

            // Maintain bounded event list (oldest dropped first).
            entry.RecentEvents.Add(securityEvent);
            while (entry.RecentEvents.Count > _options.MaxEventsPerNode)
                entry.RecentEvents.RemoveAt(0);

            var current = entry.TrustScore;

            if (Math.Abs(current - previous) > 0.0001)
                Publish(entry.NodeId, previous, current,
                    securityEvent.Description, securityEvent.OccurredAt);

            return (previous, current);
        }
    }

    /// <summary>
    /// Passively heals all tracked peers by <c>RecoveryRatePerSecond × elapsed</c>.
    /// Peers already at 1.0 are skipped. Called by the background recovery timer.
    /// </summary>
    public void ApplyRecovery(TimeSpan elapsed)
    {
        var amount = _options.RecoveryRatePerSecond * elapsed.TotalSeconds;
        if (amount <= 0) return;

        foreach (var entry in _nodes.Values)
        {
            lock (entry)
            {
                if (entry.TrustScore >= 1.0) continue;

                var previous      = entry.TrustScore;
                entry.TrustScore  = Math.Min(1.0, previous + amount);
                entry.LastUpdated = DateTimeOffset.UtcNow;

                Publish(entry.NodeId, previous, entry.TrustScore,
                    "passive-recovery", DateTimeOffset.UtcNow);
            }
        }
    }

    // ─── History queries ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns events for <paramref name="nodeId"/> that fall within
    /// <see cref="SecurityOptions.EventWindow"/> of now.
    /// Returns an empty list for unknown peers.
    /// </summary>
    public IReadOnlyList<PeerSecurityEvent> GetRecentEvents(string nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var entry)) return [];

        var cutoff = DateTimeOffset.UtcNow - _options.EventWindow;
        lock (entry)
            return entry.RecentEvents
                .Where(e => e.OccurredAt >= cutoff)
                .ToList();
    }

    // ─── Private ──────────────────────────────────────────────────────────────

    private void Publish(
        string nodeId, double previous, double current,
        string reason, DateTimeOffset at)
    {
        _channel.Writer.TryWrite(
            new PeerTrustScoreUpdate(nodeId, previous, current, reason, at));
    }
}
