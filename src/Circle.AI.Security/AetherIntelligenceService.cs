namespace Circle.AI.Security;

using System.Runtime.CompilerServices;
using Circle.AI.Aether;

// ─────────────────────────────────────────────────────────────────────────────
// Intelligence output — full implementation of IAetherIntelligence.
//
// Reads trust scores and event history from NodeTrustRegistry and packages
// them as the four intelligence outputs consumed by apps and the Security Layer:
//
//   NetworkHealthReport     aggregate mesh health (overall score, counts)
//   ThreatAssessment        per-node confidence + level + indicators
//   RoutingAdvice           trust-aware path with avoid-list
//   TrustScoreUpdate stream live channel of every score change
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Reads <see cref="NodeTrustRegistry"/> state to produce BhenguAI intelligence
/// outputs. Wires directly to the registry's <see cref="NodeTrustRegistry.TrustScoreUpdates"/>
/// channel for the streaming API.
/// </summary>
public sealed class AetherIntelligenceService : IAetherIntelligence
{
    private readonly NodeTrustRegistry _registry;
    private readonly SecurityOptions   _options;

    public AetherIntelligenceService(NodeTrustRegistry registry, SecurityOptions options)
    {
        _registry = registry;
        _options  = options;
    }

    // ─── IAetherIntelligence ─────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<NetworkHealthReport> GetNetworkHealthAsync(CancellationToken ct = default)
    {
        var nodeIds = _registry.AllNodeIds.ToList();

        if (nodeIds.Count == 0)
        {
            return Task.FromResult(new NetworkHealthReport(
                OverallScore:        1.0,
                TrustedNodeCount:    0,
                SuspiciousNodeCount: 0,
                Summary:             "No nodes observed.",
                GeneratedAt:         DateTimeOffset.UtcNow));
        }

        var scores   = nodeIds.Select(id => _registry.GetTrustScore(id)).ToList();
        var overall  = scores.Average();
        var trusted  = scores.Count(s => s > _options.AvoidNodeThreshold);
        var suspicious = scores.Count(s => s <= _options.ElevateMonitoringThreshold);

        var summary = overall switch
        {
            > 0.90 => "Mesh health is excellent.",
            > 0.75 => "Mesh health is good; minor anomalies detected.",
            > 0.50 => "Mesh health is degraded; elevated monitoring active.",
            > 0.25 => "Mesh health is poor; routing around compromised nodes.",
            _      => "Mesh health is critical; quarantine directives in effect.",
        };

        return Task.FromResult(new NetworkHealthReport(
            overall, trusted, suspicious, summary, DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public Task<ThreatAssessment> AssessThreatAsync(
        string nodeId, CancellationToken ct = default)
    {
        var score      = _registry.GetTrustScore(nodeId);
        var deficit    = 1.0 - score;  // 0 = fully trusted, 1 = fully lost

        var indicators = ThreatDetector.DetectIndicators(
            _registry.GetRecentEvents(nodeId), _options.EventWindow);

        var level = score switch
        {
            <= 0.25 => AetherThreatLevel.Critical,
            <= 0.50 => AetherThreatLevel.High,
            <= 0.75 => AetherThreatLevel.Medium,
            <= 0.90 => AetherThreatLevel.Low,
            _       => AetherThreatLevel.None,
        };

        // Confidence is proportional to trust deficit, boosted by each indicator
        var confidence = Math.Min(1.0, deficit + indicators.Count * 0.1);

        return Task.FromResult(new ThreatAssessment(
            nodeId, confidence, level, indicators, DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public Task<RoutingAdvice> GetRoutingAdviceAsync(
        string destinationNodeId, CancellationToken ct = default)
    {
        var allNodes   = _registry.AllNodeIds.ToList();
        var avoidNodes = allNodes
            .Where(id => _registry.GetTrustScore(id) <= _options.AvoidNodeThreshold)
            .ToList();

        var destScore = _registry.GetTrustScore(destinationNodeId);

        // Recommended path is direct only when destination is above avoid-threshold
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

        return Task.FromResult(new RoutingAdvice(
            destinationNodeId,
            recommended,
            avoidNodes,
            Confidence: destScore,
            reasoning,
            DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TrustScoreUpdate> StreamTrustScoresAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var update in _registry.TrustScoreUpdates.ReadAllAsync(ct))
            yield return update;
    }
}
