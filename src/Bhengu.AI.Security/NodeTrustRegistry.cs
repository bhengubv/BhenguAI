namespace Bhengu.AI.Security;

using System.Collections.Concurrent;
using System.Threading.Channels;
using Bhengu.AI.Aether;

// ─────────────────────────────────────────────────────────────────────────────
// Thread-safe, per-node trust store.
//
// - Each node gets a score in [0, 1]. 1.0 = fully trusted; 0.0 = fully lost.
// - ApplyDegradation drops the score and records the triggering event.
// - ApplyRecovery heals all nodes passively (called by a background timer).
// - TrustScoreUpdates is an unbounded channel; readers receive every change.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Per-node mutable trust state. Exposed for diagnostics and tests.</summary>
public sealed class NodeTrustEntry
{
    public required string NodeId  { get; init; }
    public double TrustScore       { get; internal set; }
    public DateTimeOffset LastUpdated { get; internal set; } = DateTimeOffset.UtcNow;

    /// <summary>Bounded history of security events (oldest-first).</summary>
    public List<AetherSecurityEvent> RecentEvents { get; } = new();
}

/// <summary>
/// Maintains per-node trust scores, event history, and a live channel of
/// trust score changes consumed by <see cref="AetherIntelligenceService"/>.
/// </summary>
public sealed class NodeTrustRegistry
{
    private readonly SecurityOptions _options;
    private readonly ConcurrentDictionary<string, NodeTrustEntry> _nodes = new();

    // Single writer / multiple reader — matches one background recovery loop
    // writing and multiple intelligence/intelligence subscribers reading.
    private readonly Channel<TrustScoreUpdate> _channel =
        Channel.CreateUnbounded<TrustScoreUpdate>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    public NodeTrustRegistry(SecurityOptions options) => _options = options;

    /// <summary>
    /// Stream of trust score changes; never completes during normal operation.
    /// Callers should pass a <see cref="CancellationToken"/> to break out.
    /// </summary>
    public ChannelReader<TrustScoreUpdate> TrustScoreUpdates => _channel.Reader;

    // ─── Node access ─────────────────────────────────────────────────────────

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

    /// <summary>All node IDs currently tracked.</summary>
    public IEnumerable<string> AllNodeIds => _nodes.Keys;

    /// <summary>
    /// Returns the current trust score for <paramref name="nodeId"/>,
    /// or <see cref="SecurityOptions.InitialTrustScore"/> for unknown nodes.
    /// </summary>
    public double GetTrustScore(string nodeId)
    {
        if (_nodes.TryGetValue(nodeId, out var entry))
            lock (entry) return entry.TrustScore;

        return _options.InitialTrustScore;
    }

    // ─── Mutations ───────────────────────────────────────────────────────────

    /// <summary>
    /// Applies trust degradation for a security event.
    /// Score is clamped to [0, 1]; the event is appended to the per-node
    /// history; a <see cref="TrustScoreUpdate"/> is published on the channel.
    /// Returns <c>(previousScore, newScore)</c>.
    /// </summary>
    public (double Previous, double Current) ApplyDegradation(
        AetherSecurityEvent securityEvent, double degradationAmount)
    {
        var entry = GetOrCreate(securityEvent.NodeId);

        lock (entry)
        {
            var previous = entry.TrustScore;
            entry.TrustScore  = Math.Clamp(previous - degradationAmount, 0.0, 1.0);
            entry.LastUpdated = securityEvent.OccurredAt;

            // Maintain bounded event list (oldest dropped)
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
    /// Passively heals all tracked nodes by <c>RecoveryRatePerSecond × elapsed</c>.
    /// Nodes already at 1.0 are skipped. Called by the background recovery timer.
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

                var previous = entry.TrustScore;
                entry.TrustScore  = Math.Min(1.0, previous + amount);
                entry.LastUpdated = DateTimeOffset.UtcNow;

                Publish(entry.NodeId, previous, entry.TrustScore,
                    "passive-recovery", DateTimeOffset.UtcNow);
            }
        }
    }

    // ─── History queries ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns events for <paramref name="nodeId"/> that fall within
    /// <see cref="SecurityOptions.EventWindow"/> of now.
    /// Returns an empty list for unknown nodes.
    /// </summary>
    public IReadOnlyList<AetherSecurityEvent> GetRecentEvents(string nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var entry)) return [];

        var cutoff = DateTimeOffset.UtcNow - _options.EventWindow;
        lock (entry)
            return entry.RecentEvents
                .Where(e => e.OccurredAt >= cutoff)
                .ToList();
    }

    // ─── Private ─────────────────────────────────────────────────────────────

    private void Publish(
        string nodeId, double previous, double current,
        string reason, DateTimeOffset at)
    {
        _channel.Writer.TryWrite(
            new TrustScoreUpdate(nodeId, previous, current, reason, at));
    }
}
