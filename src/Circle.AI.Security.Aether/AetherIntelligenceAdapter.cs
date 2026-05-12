namespace Circle.AI.Security.Aether;

using Circle.AI.Aether;
using Circle.AI.Security;

// ─────────────────────────────────────────────────────────────────────────────
// AetherIntelligenceAdapter
//
// Implements the Aether IAetherIntelligence contract by delegating to the
// transport-agnostic PeerIntelligenceService and mapping result types:
//
//   PeerNetworkHealthReport → NetworkHealthReport
//   PeerThreatAssessment    → ThreatAssessment
//   PeerRoutingAdvice       → RoutingAdvice
//   PeerTrustScoreUpdate    → TrustScoreUpdate (streaming)
//
// Callers that only need transport-agnostic intelligence should use
// PeerIntelligenceService (Circle.AI.Security) directly.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Implements <see cref="IAetherIntelligence"/> by wrapping
/// <see cref="PeerIntelligenceService"/> and mapping transport-agnostic
/// result types to their Aether equivalents.
/// </summary>
public sealed class AetherIntelligenceAdapter : IAetherIntelligence
{
    private readonly PeerIntelligenceService _inner;

    public AetherIntelligenceAdapter(PeerIntelligenceService inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    // ─── IAetherIntelligence ──────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<NetworkHealthReport> GetNetworkHealthAsync(CancellationToken ct = default)
    {
        var r = await _inner.GetNetworkHealthAsync(ct);
        return new NetworkHealthReport(
            r.OverallScore,
            r.TrustedPeerCount,
            r.SuspiciousPeerCount,
            r.Summary,
            r.GeneratedAt);
    }

    /// <inheritdoc />
    public async Task<ThreatAssessment> AssessThreatAsync(
        string nodeId, CancellationToken ct = default)
    {
        var a = await _inner.AssessThreatAsync(nodeId, ct);
        return new ThreatAssessment(
            a.NodeId,
            a.Confidence,
            AetherMapper.ToAetherThreatLevel(a.ThreatLevel),
            a.Indicators,
            a.AssessedAt);
    }

    /// <inheritdoc />
    public async Task<RoutingAdvice> GetRoutingAdviceAsync(
        string destinationNodeId, CancellationToken ct = default)
    {
        var r = await _inner.GetRoutingAdviceAsync(destinationNodeId, ct);
        return new RoutingAdvice(
            r.DestinationNodeId,
            r.RecommendedPath,
            r.AvoidNodeIds,
            r.Confidence,
            r.Reasoning,
            r.GeneratedAt);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TrustScoreUpdate> StreamTrustScoresAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken ct = default)
    {
        await foreach (var u in _inner.StreamTrustScoresAsync(ct))
        {
            yield return new TrustScoreUpdate(
                u.NodeId,
                u.PreviousScore,
                u.NewScore,
                u.Reason,
                u.ChangedAt);
        }
    }
}
