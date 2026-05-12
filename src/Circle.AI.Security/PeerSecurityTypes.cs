// PeerSecurityTypes.cs
//
// Transport-agnostic security primitives for Circle.AI.Security.
//
// These types are deliberately free of any transport dependency (Aether, WiFi,
// BLE, NearLink, HTTP, etc.).  Every transport adapter translates its own event
// vocabulary into these types before feeding the security layer.
//
// Type map:
//   PeerSecurityEventKind  — what happened (transport-neutral event category)
//   PeerThreatLevel        — how severe (None → Critical)
//   PeerSecurityEvent      — one security incident from any transport
//   PeerDirectiveKind      — what the security layer recommends (ElevateMonitoring, AvoidNode, Quarantine, Release)
//   PeerDirective          — a directive issued to all IPeerDirectiveConsumer subscribers
//   PeerTrustScoreUpdate   — one change notification emitted by NodeTrustRegistry
//   PeerSecurityPosture    — aggregate snapshot of security state
//   PeerNetworkHealthReport  — aggregate health across all observed peers
//   PeerThreatAssessment   — per-node threat confidence + indicators
//   PeerRoutingAdvice      — trust-aware path recommendation
//
// Interfaces:
//   IPeerDirectiveConsumer — receives PeerDirective instances from any security layer
//   IPeerSecurityLayer     — lifecycle + query surface for the transport-agnostic layer
//   IPeerIntelligence      — read-only intelligence queries (health, threat, routing)
//   IPeerSecurityEventFeed — implemented by transport adapters to register an event source

namespace Circle.AI.Security;

using System.Runtime.CompilerServices;

// ── Enumerations ─────────────────────────────────────────────────────────────

/// <summary>
/// Transport-neutral classification of a peer security event.
/// </summary>
public enum PeerSecurityEventKind
{
    /// <summary>Authentication attempt (login, handshake, re-auth).</summary>
    AuthAttempt,

    /// <summary>Anomalous routing behaviour detected (loop, black-hole, etc.).</summary>
    RoutingAnomaly,

    /// <summary>Peer behaviour changed unexpectedly (rate, pattern, protocol).</summary>
    BehaviourChange,

    /// <summary>Encryption negotiation event (downgrade, cipher mismatch).</summary>
    EncryptionEvent,

    /// <summary>Active intrusion probe or exploitation attempt.</summary>
    IntrusionSignal,

    /// <summary>Privilege escalation or capability violation attempt.</summary>
    PrivilegeAttempt,

    /// <summary>Unusual connection pattern (port scan, rapid reconnect).</summary>
    ConnectionAnomaly,

    /// <summary>Suspected data exfiltration (volume, destination anomaly).</summary>
    DataExfiltration,

    /// <summary>Denial-of-service signal (flooding, resource exhaustion).</summary>
    DenialOfService,

    /// <summary>Catch-all for events that do not map to a specific category.</summary>
    Unknown,
}

/// <summary>
/// Severity level for a peer security event or threat assessment.
/// Values match the intuitive ordering: None is safest, Critical is worst.
/// </summary>
public enum PeerThreatLevel
{
    /// <summary>No threat — event carries no security significance.</summary>
    None = 0,

    /// <summary>Low-level anomaly — monitor but no action required.</summary>
    Low = 1,

    /// <summary>Notable anomaly — elevated monitoring recommended.</summary>
    Medium = 2,

    /// <summary>Significant threat — routing around the peer recommended.</summary>
    High = 3,

    /// <summary>Active or confirmed attack — quarantine the peer.</summary>
    Critical = 4,
}

/// <summary>
/// The action recommended by the security layer for a given peer.
/// </summary>
public enum PeerDirectiveKind
{
    /// <summary>Increase observation cadence; no traffic restriction yet.</summary>
    ElevateMonitoring,

    /// <summary>Exclude the peer from routing; still accept inbound connections.</summary>
    AvoidNode,

    /// <summary>Hard-block the peer — no traffic to or from it.</summary>
    QuarantineNode,

    /// <summary>
    /// Lift a previous directive; the peer has recovered sufficient trust.
    /// Not issued automatically — requires explicit operator action.
    /// </summary>
    ReleaseNode,
}

// ── Records ───────────────────────────────────────────────────────────────────

/// <summary>
/// One security incident observed on any transport.
/// </summary>
/// <param name="NodeId">Stable identifier of the peer that generated the event.</param>
/// <param name="Kind">Transport-neutral event category.</param>
/// <param name="ThreatLevel">Assessed severity at the time of observation.</param>
/// <param name="Description">Human-readable description of the event.</param>
/// <param name="TransportId">
/// Identifier for the transport that produced the event
/// (e.g. <c>"aether"</c>, <c>"wifi"</c>, <c>"ble"</c>, <c>"nearlink"</c>, <c>"http"</c>).
/// </param>
/// <param name="OccurredAt">UTC timestamp of the event.</param>
public sealed record PeerSecurityEvent(
    string NodeId,
    PeerSecurityEventKind Kind,
    PeerThreatLevel ThreatLevel,
    string Description,
    string TransportId,
    DateTimeOffset OccurredAt);

/// <summary>
/// A security directive issued to all registered <see cref="IPeerDirectiveConsumer"/>
/// subscribers when a peer's trust crosses a threshold.
/// </summary>
/// <param name="Kind">The recommended action.</param>
/// <param name="TargetNodeId">The peer to which the directive applies.</param>
/// <param name="TrustScore">Current trust score of the peer at time of issue.</param>
/// <param name="ThreatLevel">Threat level at time of issue.</param>
/// <param name="Reason">Human-readable explanation for the directive.</param>
/// <param name="Duration">
/// Optional duration after which the directive should be re-evaluated.
/// <c>null</c> means permanent until an explicit <see cref="PeerDirectiveKind.ReleaseNode"/>
/// directive is issued.
/// </param>
/// <param name="IssuedAt">UTC timestamp of issue.</param>
public sealed record PeerDirective(
    PeerDirectiveKind Kind,
    string TargetNodeId,
    double TrustScore,
    PeerThreatLevel ThreatLevel,
    string Reason,
    TimeSpan? Duration,
    DateTimeOffset IssuedAt);

/// <summary>
/// Notification emitted by <see cref="NodeTrustRegistry"/> whenever a node's
/// trust score changes.
/// </summary>
/// <param name="NodeId">The peer whose score changed.</param>
/// <param name="PreviousScore">Score before this change.</param>
/// <param name="NewScore">Score after this change.</param>
/// <param name="Reason">Short description of the cause (event description or "passive-recovery").</param>
/// <param name="ChangedAt">UTC timestamp of the change.</param>
public sealed record PeerTrustScoreUpdate(
    string NodeId,
    double PreviousScore,
    double NewScore,
    string Reason,
    DateTimeOffset ChangedAt);

/// <summary>
/// Snapshot of the overall security posture across all observed peers.
/// </summary>
/// <param name="OverallThreatLevel">Worst-case threat level in the current peer set.</param>
/// <param name="QuarantinedPeerCount">Number of peers at or below <see cref="SecurityOptions.QuarantineThreshold"/>.</param>
/// <param name="MonitoredPeerCount">Number of peers elevated beyond monitoring threshold but not yet quarantined.</param>
/// <param name="IsActive">Whether the security layer is currently running.</param>
/// <param name="GeneratedAt">UTC timestamp of this snapshot.</param>
public sealed record PeerSecurityPosture(
    PeerThreatLevel OverallThreatLevel,
    int QuarantinedPeerCount,
    int MonitoredPeerCount,
    bool IsActive,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Aggregate network health across all observed peers.
/// </summary>
/// <param name="OverallScore">Average trust score [0.0, 1.0] across all peers.</param>
/// <param name="TrustedPeerCount">Peers above <see cref="SecurityOptions.AvoidNodeThreshold"/>.</param>
/// <param name="SuspiciousPeerCount">Peers at or below <see cref="SecurityOptions.ElevateMonitoringThreshold"/>.</param>
/// <param name="Summary">Human-readable health summary.</param>
/// <param name="GeneratedAt">UTC timestamp of this report.</param>
public sealed record PeerNetworkHealthReport(
    double OverallScore,
    int TrustedPeerCount,
    int SuspiciousPeerCount,
    string Summary,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Per-peer threat assessment: confidence score, threat level, and detected indicators.
/// </summary>
/// <param name="NodeId">The assessed peer.</param>
/// <param name="Confidence">
/// Likelihood that the peer is a genuine threat [0.0, 1.0].
/// Derived from trust deficit + indicator count.
/// </param>
/// <param name="ThreatLevel">Classified severity.</param>
/// <param name="Indicators">Human-readable indicator tags (e.g. "brute-force-auth", "intrusion-signal").</param>
/// <param name="AssessedAt">UTC timestamp of this assessment.</param>
public sealed record PeerThreatAssessment(
    string NodeId,
    double Confidence,
    PeerThreatLevel ThreatLevel,
    IReadOnlyList<string> Indicators,
    DateTimeOffset AssessedAt);

/// <summary>
/// Trust-aware routing recommendation for reaching a destination peer.
/// </summary>
/// <param name="DestinationNodeId">The target peer.</param>
/// <param name="RecommendedPath">
/// Ordered list of peer IDs forming the recommended path.
/// Empty when no safe path is available.
/// </param>
/// <param name="AvoidNodeIds">Peers that should be excluded from routing.</param>
/// <param name="Confidence">Confidence in the recommendation [0.0, 1.0].</param>
/// <param name="Reasoning">Human-readable explanation.</param>
/// <param name="GeneratedAt">UTC timestamp of this advice.</param>
public sealed record PeerRoutingAdvice(
    string DestinationNodeId,
    IReadOnlyList<string> RecommendedPath,
    IReadOnlyList<string> AvoidNodeIds,
    double Confidence,
    string Reasoning,
    DateTimeOffset GeneratedAt);

// ── Interfaces ────────────────────────────────────────────────────────────────

/// <summary>
/// Receives security directives from any <see cref="IPeerSecurityLayer"/> implementation.
/// </summary>
public interface IPeerDirectiveConsumer
{
    /// <summary>Called when the security layer issues a directive for a peer.</summary>
    void OnDirective(PeerDirective directive);
}

/// <summary>
/// Transport-agnostic security layer lifecycle and posture surface.
/// </summary>
public interface IPeerSecurityLayer
{
    /// <summary>Starts the background trust-recovery loop.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Stops the recovery loop and releases resources.</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Feed a security event from any transport into the security layer.
    /// The layer will degrade the peer's trust score and issue directives as needed.
    /// </summary>
    void HandlePeerEvent(PeerSecurityEvent e);

    /// <summary>Subscribe to receive directives. Dispose the returned handle to unsubscribe.</summary>
    IDisposable SubscribeToDirectives(IPeerDirectiveConsumer consumer);

    /// <summary>Returns a snapshot of the current security posture.</summary>
    Task<PeerSecurityPosture> GetPostureAsync(CancellationToken ct = default);
}

/// <summary>
/// Transport-agnostic intelligence queries over accumulated trust data.
/// </summary>
public interface IPeerIntelligence
{
    /// <summary>Returns aggregate network health across all observed peers.</summary>
    Task<PeerNetworkHealthReport> GetNetworkHealthAsync(CancellationToken ct = default);

    /// <summary>Returns a threat assessment for a specific peer.</summary>
    Task<PeerThreatAssessment> AssessThreatAsync(string nodeId, CancellationToken ct = default);

    /// <summary>Returns trust-aware routing advice toward a destination peer.</summary>
    Task<PeerRoutingAdvice> GetRoutingAdviceAsync(string destinationNodeId, CancellationToken ct = default);

    /// <summary>
    /// Streams every trust score change as they occur.
    /// Completes when <paramref name="ct"/> is cancelled.
    /// </summary>
    IAsyncEnumerable<PeerTrustScoreUpdate> StreamTrustScoresAsync(
        CancellationToken ct = default);
}

/// <summary>
/// Implemented by transport adapters to register an event source with the security layer.
/// The security layer calls <see cref="StartAsync"/> once to begin pumping events.
/// </summary>
public interface IPeerSecurityEventFeed
{
    /// <summary>Human-readable identifier for this transport (e.g. "wifi", "ble", "aether").</summary>
    string TransportId { get; }

    /// <summary>
    /// Begins feeding events into <paramref name="handler"/> until
    /// <paramref name="ct"/> is cancelled.
    /// </summary>
    Task StartAsync(Action<PeerSecurityEvent> handler, CancellationToken ct);
}
