namespace Bhengu.AI.Security;

// ─────────────────────────────────────────────────────────────────────────────
// Configuration model for the AI Security Layer.
//
// All threshold values are trust scores in the [0, 1] range.
// Lower score = more compromised. Thresholds must satisfy:
//   QuarantineThreshold < AvoidNodeThreshold < ElevateMonitoringThreshold
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Configures thresholds, decay rates, and event retention for the
/// AI Security Layer. Pass to <see cref="NodeTrustRegistry"/> and
/// <see cref="AISecurityLayerService"/> via DI.
/// </summary>
public sealed class SecurityOptions
{
    /// <summary>
    /// Trust score below which monitoring is elevated for the node.
    /// Default: 0.75 — a 25 % trust loss triggers closer observation.
    /// </summary>
    public double ElevateMonitoringThreshold { get; set; } = 0.75;

    /// <summary>
    /// Trust score below which the node is excluded from routing.
    /// Default: 0.50 — half trust lost → route around the node.
    /// </summary>
    public double AvoidNodeThreshold { get; set; } = 0.50;

    /// <summary>
    /// Trust score at or below which the node is hard-blocked (quarantined).
    /// Default: 0.25 — severe compromise → no traffic to or from the node.
    /// </summary>
    public double QuarantineThreshold { get; set; } = 0.25;

    /// <summary>
    /// Passive trust recovery per second when no adverse events occur.
    /// Default: 0.001 ≈ full recovery from zero in ~16 minutes of clean behaviour.
    /// </summary>
    public double RecoveryRatePerSecond { get; set; } = 0.001;

    /// <summary>
    /// Sliding window used for pattern-based indicator detection (e.g. repeated
    /// auth attempts). Events outside this window are ignored for pattern analysis.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan EventWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum security events retained per node. Oldest are dropped first.
    /// Default: 100.
    /// </summary>
    public int MaxEventsPerNode { get; set; } = 100;

    /// <summary>
    /// Trust score assigned to nodes on first observation.
    /// Default: 1.0 (full trust until evidence says otherwise).
    /// </summary>
    public double InitialTrustScore { get; set; } = 1.0;
}
