namespace Bhengu.AI.Aether;

// ──────────────────────────────────────────────────────────────────────────
// Contract 4 — Security Layer
//
// BhenguAI reasons over Aether telemetry and publishes SecurityDirectives.
// Aether's policy engine consumes those directives — but Aether never calls
// into BhenguAI directly. The boundary is strictly one-way.
//
// External Aether adopters can opt in to this contract by implementing
// ISecurityDirectiveConsumer on their policy engine. It is never mandatory.
// ──────────────────────────────────────────────────────────────────────────

/// <summary>The action BhenguAI is recommending to Aether's policy engine.</summary>
public enum SecurityDirectiveKind
{
    /// <summary>Adjust the recorded trust score for a node.</summary>
    UpdateNodeTrust,

    /// <summary>Exclude the node from routing decisions (soft block).</summary>
    AvoidNode,

    /// <summary>Hard block — no traffic to or from the node until released.</summary>
    QuarantineNode,

    /// <summary>Lift an AvoidNode or QuarantineNode directive.</summary>
    ReleaseNode,

    /// <summary>Request that the user re-authenticates before a sensitive operation.</summary>
    RequestReauth,

    /// <summary>Increase telemetry verbosity for the target node.</summary>
    ElevateMonitoring,
}

/// <summary>
/// An instruction published by the AI Security Layer to Aether's policy
/// engine. Aether is never required to honour a directive — adoption is a
/// policy decision for each deployment.
/// </summary>
public sealed record SecurityDirective(
    SecurityDirectiveKind Kind,
    string? TargetNodeId,
    double? TrustScoreOverride,
    AetherThreatLevel ThreatLevel,
    string Reason,
    TimeSpan? Duration,
    DateTimeOffset IssuedAt)
{
    /// <summary>True when the directive targets a specific node.</summary>
    public bool HasTarget => !string.IsNullOrWhiteSpace(TargetNodeId);

    /// <summary>True when Duration is null — the directive has no automatic expiry.</summary>
    public bool IsPermanent => Duration is null;
}

/// <summary>
/// Point-in-time summary of the AI Security Layer's current posture.
/// </summary>
public sealed record SecurityPosture(
    AetherThreatLevel OverallThreatLevel,
    int QuarantinedNodeCount,
    int MonitoredNodeCount,
    bool IsActive,
    DateTimeOffset AssessedAt);

/// <summary>
/// Receives security directives from the AI Security Layer.
/// Implement this on Aether's policy engine to participate in AI-guided
/// security decisions.
/// </summary>
public interface ISecurityDirectiveConsumer
{
    /// <summary>
    /// Called each time BhenguAI issues a security directive.
    /// Implementations decide whether and how to honour it.
    /// </summary>
    void OnDirective(SecurityDirective directive);
}

/// <summary>
/// The AI Security Layer contract. BhenguAI implements this by subscribing
/// to <see cref="IAetherTelemetry"/> and producing
/// <see cref="SecurityDirective"/> outputs consumed by Aether's policy
/// engine via <see cref="ISecurityDirectiveConsumer"/>.
/// </summary>
public interface IAISecurityLayer
{
    /// <summary>
    /// Wire the security layer to an Aether telemetry feed and begin
    /// processing events.
    /// </summary>
    Task StartAsync(IAetherTelemetry telemetry, CancellationToken ct = default);

    /// <summary>Stop processing and release all telemetry subscriptions.</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Subscribe a policy engine to receive security directives.
    /// Dispose the returned handle to unsubscribe.
    /// </summary>
    IDisposable SubscribeToDirectives(ISecurityDirectiveConsumer consumer);

    /// <summary>Returns the current security posture snapshot.</summary>
    Task<SecurityPosture> GetPostureAsync(CancellationToken ct = default);
}
