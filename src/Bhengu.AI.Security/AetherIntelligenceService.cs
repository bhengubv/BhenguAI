namespace Bhengu.AI.Security;

using System.Runtime.CompilerServices;

// ─────────────────────────────────────────────────────────────────────────────
// Transport-agnostic intelligence output — full implementation of IPeerIntelligence.
//
// Reads trust scores and event history from NodeTrustRegistry and packages
// them as the four intelligence outputs consumed by apps and the security layer:
//
//   PeerNetworkHealthReport   aggregate health (overall score, counts)
//   PeerThreatAssessment      per-peer confidence + level + indicators
//   PeerRoutingAdvice         trust-aware path with avoid-list
//   PeerTrustScoreUpdate      live channel of every score change
//
// No dependency on any transport package.  Transports that expose their own
// intelligence interface (e.g. IAetherIntelligence) use an adapter in the
// corresponding bridge package (Bhengu.AI.Security.Aether).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Reads <see cref="NodeTrustRegistry"/> state to produce transport-agnostic
/// intelligence outputs. Wires directly to the registry's
/// <see cref="NodeTrustRegistry.TrustScoreUpdates"/> channel for the streaming API.
/// </summary>
public sealed class PeerIntelligenceService : IPeerIntelligence
{
    private readonly NodeTrustRegistry _registry;
    private readonly SecurityOptions   _options;

    public PeerIntelligenceService(NodeTrustRegistry registry, SecurityOptions options)
    {
        _registry = registry;
        _options  = options;
    }

    // ─── IPeerIntelligence ────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<PeerNetworkHealthReport> GetNetworkHealthAsync(CancellationToken ct = default)
    {
        var nodeIds = _registry.AllNodeIds.ToList();

        if (nodeIds.Count == 0)
        {
            return Task.FromResult(new PeerNetworkHealthReport(
                OverallScore:       1.0,
                TrustedPeerCount:   0,
                SuspiciousPeerCount: 0,
                Summary:            "No peers observed.",
                GeneratedAt:        DateTimeOffset.UtcNow));
        }

        var scores     = nodeIds.Select(id => _registry.GetTrustScore(id)).ToList();
        var overall    = scores.Average();
        var trusted    = scores.Count(s => s > _options.AvoidNodeThreshold);
        var suspicious = scores.Count(s => s <= _options.ElevateMonitoringThreshold);

        var summary = overall switch
        {
            > 0.90 => "Network health is excellent.",
            > 0.75 => "Network health is good; minor anomalies detected.",
            > 0.50 => "Network health is degraded; elevated monitoring active.",
            > 0.25 => "Network health is poor; routing around compromised peers.",
            _      => "Network health is critical; quarantine directives in effect.",
        };

        return Task.FromResult(new PeerNetworkHealthReport(
            overall, trusted, suspicious, summary, DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public Task<PeerThreatAssessment> AssessThreatAsync(
        string nodeId, CancellationToken ct = default)
    {
        var score   = _registry.GetTrustScore(nodeId);
        var deficit = 1.0 - score;   // 0 = fully trusted, 1 = fully lost

        var indicators = ThreatDetector.DetectIndicators(
            _registry.GetRecentEvents(nodeId), _options.EventWindow);

        var level = score switch
        {
            <= 0.25 => PeerThreatLevel.Critical,
            <= 0.50 => PeerThreatLevel.High,
            <= 0.75 => PeerThreatLevel.Medium,
            <= 0.90 => PeerThreatLevel.Low,
            _       => PeerThreatLevel.None,
        };

        // Confidence is proportional to trust deficit, boosted by each indicator.
        var confidence = Math.Min(1.0, deficit + indicators.Count * 0.1);

        return Task.FromResult(new PeerThreatAssessment(
            nodeId, confidence, level, indicators, DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public Task<PeerRoutingAdvice> GetRoutingAdviceAsync(
        string destinationNodeId, CancellationToken ct = default)
    {
        var allNodes   = _registry.AllNodeIds.ToList();
        var avoidNodes = allNodes
            .Where(id => _registry.GetTrustScore(id) <= _options.AvoidNodeThreshold)
            .ToList();

        var destScore = _registry.GetTrustScore(destinationNodeId);

        // Recommended path is direct only when destination is above avoid-threshold.
        var recommended = destScore > _options.AvoidNodeThreshold
            ? (IReadOnlyList<string>)[destinationNodeId]
            : [];

        var reasoning = destScore switch
        {
            > 0.75 => $"Direct path to {destinationNodeId} is trusted (score {destScore:F2}).",
            > 0.50 => $"Destination {destinationNodeId} is under monitoring; routing with caution.",
            > 0.25 => $"Destination {destinationNodeId} has degraded trust; avoid recommended.",
            _      => $"Destination {destinationNodeId} is quarantined; no safe path available.",
        };

        return Task.FromResult(new PeerRoutingAdvice(
            destinationNodeId,
            recommended,
            avoidNodes,
            Confidence: destScore,
            reasoning,
            DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PeerTrustScoreUpdate> StreamTrustScoresAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var update in _registry.TrustScoreUpdates.ReadAllAsync(ct))
            yield return update;
    }
}
