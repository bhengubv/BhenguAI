namespace Bhengu.AI.Security;

using Bhengu.AI.Aether;

// ─────────────────────────────────────────────────────────────────────────────
// Pure static threat logic — no state, no DI, fully testable in isolation.
//
// Two responsibilities:
//   1. ComputeDegradation: how much trust a single security event should cost.
//   2. DetectIndicators:   which behavioural patterns are visible in a window.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Stateless threat analysis helpers used by <see cref="AISecurityLayerService"/>
/// and <see cref="AetherIntelligenceService"/>.
/// </summary>
public static class ThreatDetector
{
    // ─── Degradation weights by event kind ───────────────────────────────────

    private static double BaseWeight(AetherSecurityEventKind kind) => kind switch
    {
        AetherSecurityEventKind.NodeAuthAttempt     => 0.05,
        AetherSecurityEventKind.RoutingAnomaly      => 0.10,
        AetherSecurityEventKind.NodeBehaviourChange => 0.08,
        AetherSecurityEventKind.EncryptionEvent     => 0.06,
        AetherSecurityEventKind.IntrusionSignal     => 0.15,
        AetherSecurityEventKind.PrivilegeAttempt    => 0.12,
        _                                           => 0.05,
    };

    // ─── Multipliers by threat level ─────────────────────────────────────────

    private static double ThreatMultiplier(AetherThreatLevel level) => level switch
    {
        AetherThreatLevel.None     => 0.0,
        AetherThreatLevel.Low      => 0.5,
        AetherThreatLevel.Medium   => 1.0,
        AetherThreatLevel.High     => 2.0,
        AetherThreatLevel.Critical => 3.0,
        _                          => 1.0,
    };

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the trust-score degradation amount for a security event,
    /// calculated as <c>BaseWeight(kind) × ThreatMultiplier(level)</c>.
    /// Returns 0 when <see cref="AetherThreatLevel.None"/>.
    /// </summary>
    public static double ComputeDegradation(AetherSecurityEvent e) =>
        BaseWeight(e.Kind) * ThreatMultiplier(e.ThreatLevel);

    /// <summary>
    /// Derives human-readable threat indicator tags from a set of recent
    /// events within the given <paramref name="window"/>.
    /// Returns an empty list when no patterns are detected.
    /// </summary>
    public static IReadOnlyList<string> DetectIndicators(
        IEnumerable<AetherSecurityEvent> recentEvents, TimeSpan window)
    {
        var cutoff  = DateTimeOffset.UtcNow - window;
        var windowed = recentEvents.Where(e => e.OccurredAt >= cutoff).ToList();

        if (windowed.Count == 0) return [];

        var indicators = new List<string>(5);

        // ≥ 3 auth attempts within the window → brute-force signal
        if (windowed.Count(e => e.Kind == AetherSecurityEventKind.NodeAuthAttempt) >= 3)
            indicators.Add("repeated-auth-attempts");

        // Any intrusion signal → explicit probe or exploit
        if (windowed.Any(e => e.Kind == AetherSecurityEventKind.IntrusionSignal))
            indicators.Add("intrusion-signal-detected");

        // High or Critical event → severity flag
        if (windowed.Any(e => e.ThreatLevel is AetherThreatLevel.High or AetherThreatLevel.Critical))
            indicators.Add("high-severity-event");

        // ≥ 3 distinct event kinds → multi-vector activity
        if (windowed.Select(e => e.Kind).Distinct().Count() >= 3)
            indicators.Add("multi-vector-activity");

        // Privilege escalation attempt
        if (windowed.Any(e => e.Kind == AetherSecurityEventKind.PrivilegeAttempt))
            indicators.Add("privilege-escalation-attempt");

        return indicators;
    }
}
