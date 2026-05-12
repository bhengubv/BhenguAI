namespace Bhengu.AI.Security;

// ─────────────────────────────────────────────────────────────────────────────
// Pure static threat logic — no state, no DI, fully testable in isolation.
//
// Two responsibilities:
//   1. ComputeDegradation: how much trust a single security event should cost.
//   2. DetectIndicators:   which behavioural patterns are visible in a window.
//
// Transport-agnostic: operates on PeerSecurityEvent / PeerSecurityEventKind /
// PeerThreatLevel — no dependency on any specific transport package.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Stateless threat analysis helpers used by <see cref="SecurityLayerService"/>
/// and <see cref="PeerIntelligenceService"/>.
/// </summary>
public static class ThreatDetector
{
    // ─── Degradation weights by event kind ───────────────────────────────────

    private static double BaseWeight(PeerSecurityEventKind kind) => kind switch
    {
        PeerSecurityEventKind.AuthAttempt        => 0.05,
        PeerSecurityEventKind.RoutingAnomaly     => 0.10,
        PeerSecurityEventKind.BehaviourChange    => 0.08,
        PeerSecurityEventKind.EncryptionEvent    => 0.06,
        PeerSecurityEventKind.IntrusionSignal    => 0.15,
        PeerSecurityEventKind.PrivilegeAttempt   => 0.12,
        PeerSecurityEventKind.ConnectionAnomaly  => 0.07,
        PeerSecurityEventKind.DataExfiltration   => 0.14,
        PeerSecurityEventKind.DenialOfService    => 0.13,
        _                                        => 0.05,
    };

    // ─── Multipliers by threat level ─────────────────────────────────────────

    private static double ThreatMultiplier(PeerThreatLevel level) => level switch
    {
        PeerThreatLevel.None     => 0.0,
        PeerThreatLevel.Low      => 0.5,
        PeerThreatLevel.Medium   => 1.0,
        PeerThreatLevel.High     => 2.0,
        PeerThreatLevel.Critical => 3.0,
        _                        => 1.0,
    };

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the trust-score degradation amount for a security event,
    /// calculated as <c>BaseWeight(kind) × ThreatMultiplier(level)</c>.
    /// Returns 0 when <see cref="PeerThreatLevel.None"/>.
    /// </summary>
    public static double ComputeDegradation(PeerSecurityEvent e) =>
        BaseWeight(e.Kind) * ThreatMultiplier(e.ThreatLevel);

    /// <summary>
    /// Derives human-readable threat indicator tags from a set of recent
    /// events within the given <paramref name="window"/>.
    /// Returns an empty list when no patterns are detected.
    /// </summary>
    public static IReadOnlyList<string> DetectIndicators(
        IEnumerable<PeerSecurityEvent> recentEvents, TimeSpan window)
    {
        var cutoff   = DateTimeOffset.UtcNow - window;
        var windowed = recentEvents.Where(e => e.OccurredAt >= cutoff).ToList();

        if (windowed.Count == 0) return [];

        var indicators = new List<string>(6);

        // ≥ 3 auth attempts within the window → brute-force signal
        if (windowed.Count(e => e.Kind == PeerSecurityEventKind.AuthAttempt) >= 3)
            indicators.Add("repeated-auth-attempts");

        // Any intrusion signal → explicit probe or exploit
        if (windowed.Any(e => e.Kind == PeerSecurityEventKind.IntrusionSignal))
            indicators.Add("intrusion-signal-detected");

        // High or Critical event → severity flag
        if (windowed.Any(e => e.ThreatLevel is PeerThreatLevel.High or PeerThreatLevel.Critical))
            indicators.Add("high-severity-event");

        // ≥ 3 distinct event kinds → multi-vector activity
        if (windowed.Select(e => e.Kind).Distinct().Count() >= 3)
            indicators.Add("multi-vector-activity");

        // Privilege escalation attempt
        if (windowed.Any(e => e.Kind == PeerSecurityEventKind.PrivilegeAttempt))
            indicators.Add("privilege-escalation-attempt");

        // Data exfiltration signal
        if (windowed.Any(e => e.Kind == PeerSecurityEventKind.DataExfiltration))
            indicators.Add("data-exfiltration-signal");

        return indicators;
    }
}
