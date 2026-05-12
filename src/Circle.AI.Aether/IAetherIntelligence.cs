namespace Circle.AI.Aether;

// ──────────────────────────────────────────────────────────────────────────
// Contract 3 — Intelligence Output
//
// What BhenguAI produces after reasoning over Aether telemetry.
// Aether never sees this interface — it flows upward only.
// ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// Aggregate health of the mesh as assessed by BhenguAI.
/// </summary>
public sealed record NetworkHealthReport(
    double OverallScore,
    int TrustedNodeCount,
    int SuspiciousNodeCount,
    string Summary,
    DateTimeOffset GeneratedAt)
{
    /// <summary>True when OverallScore is within the valid 0–1 range.</summary>
    public bool IsValid => OverallScore is >= 0.0 and <= 1.0;
}

/// <summary>
/// BhenguAI's assessment of the threat posed by a specific node.
/// </summary>
public sealed record ThreatAssessment(
    string NodeId,
    double ThreatConfidence,
    AetherThreatLevel Level,
    IReadOnlyList<string> Indicators,
    DateTimeOffset AssessedAt)
{
    /// <summary>True when ThreatConfidence is within the valid 0–1 range.</summary>
    public bool IsValid => ThreatConfidence is >= 0.0 and <= 1.0;
}

/// <summary>
/// BhenguAI's recommendation for routing to a destination node, taking
/// trust scores and current threat assessments into account.
/// </summary>
public sealed record RoutingAdvice(
    string DestinationNodeId,
    IReadOnlyList<string> RecommendedPath,
    IReadOnlyList<string> AvoidNodes,
    double Confidence,
    string Reasoning,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Emitted when BhenguAI revises the trust score for a node.
/// </summary>
public sealed record TrustScoreUpdate(
    string NodeId,
    double PreviousScore,
    double CurrentScore,
    string Reason,
    DateTimeOffset UpdatedAt)
{
    /// <summary>True when the score moved in either direction.</summary>
    public bool HasChanged => Math.Abs(CurrentScore - PreviousScore) > 0.001;

    /// <summary>True when the score decreased.</summary>
    public bool IsDegraded => CurrentScore < PreviousScore;
}

/// <summary>
/// The intelligence output surface produced by BhenguAI from Aether
/// telemetry. Consumed by apps and the Security Layer; never by Aether.
/// </summary>
public interface IAetherIntelligence
{
    /// <summary>Returns an aggregate health report for the current mesh state.</summary>
    Task<NetworkHealthReport> GetNetworkHealthAsync(CancellationToken ct = default);

    /// <summary>
    /// Assesses the current threat level of a specific node.
    /// Returns a zero-confidence assessment when the node is unknown.
    /// </summary>
    Task<ThreatAssessment> AssessThreatAsync(
        string nodeId, CancellationToken ct = default);

    /// <summary>
    /// Returns a routing recommendation for reaching the given destination,
    /// factoring out nodes with low trust scores.
    /// </summary>
    Task<RoutingAdvice> GetRoutingAdviceAsync(
        string destinationNodeId, CancellationToken ct = default);

    /// <summary>
    /// Streams trust score updates as BhenguAI observes new telemetry.
    /// Useful for live dashboards and security monitoring UIs.
    /// </summary>
    IAsyncEnumerable<TrustScoreUpdate> StreamTrustScoresAsync(
        CancellationToken ct = default);
}
