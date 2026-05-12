namespace Bhengu.AI.Security.Aether;

using Bhengu.AI.Aether;
using Bhengu.AI.Security;

// ─────────────────────────────────────────────────────────────────────────────
// AetherMapper — static translation helpers between Aether and Peer types.
//
// All mappings are explicit switch expressions so new enum values added to
// either side produce a compiler warning via the default arm.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Static helpers that translate between Aether-specific types and the
/// transport-agnostic Peer types defined in <c>Bhengu.AI.Security</c>.
/// </summary>
internal static class AetherMapper
{
    // ─── AetherSecurityEventKind → PeerSecurityEventKind ─────────────────────

    internal static PeerSecurityEventKind ToPeerEventKind(AetherSecurityEventKind kind) =>
        kind switch
        {
            AetherSecurityEventKind.NodeAuthAttempt     => PeerSecurityEventKind.AuthAttempt,
            AetherSecurityEventKind.RoutingAnomaly      => PeerSecurityEventKind.RoutingAnomaly,
            AetherSecurityEventKind.NodeBehaviourChange => PeerSecurityEventKind.BehaviourChange,
            AetherSecurityEventKind.EncryptionEvent     => PeerSecurityEventKind.EncryptionEvent,
            AetherSecurityEventKind.IntrusionSignal     => PeerSecurityEventKind.IntrusionSignal,
            AetherSecurityEventKind.PrivilegeAttempt    => PeerSecurityEventKind.PrivilegeAttempt,
            _                                           => PeerSecurityEventKind.Unknown,
        };

    // ─── AetherThreatLevel ↔ PeerThreatLevel ─────────────────────────────────

    internal static PeerThreatLevel ToPeerThreatLevel(AetherThreatLevel level) =>
        level switch
        {
            AetherThreatLevel.None     => PeerThreatLevel.None,
            AetherThreatLevel.Low      => PeerThreatLevel.Low,
            AetherThreatLevel.Medium   => PeerThreatLevel.Medium,
            AetherThreatLevel.High     => PeerThreatLevel.High,
            AetherThreatLevel.Critical => PeerThreatLevel.Critical,
            _                          => PeerThreatLevel.None,
        };

    internal static AetherThreatLevel ToAetherThreatLevel(PeerThreatLevel level) =>
        level switch
        {
            PeerThreatLevel.None     => AetherThreatLevel.None,
            PeerThreatLevel.Low      => AetherThreatLevel.Low,
            PeerThreatLevel.Medium   => AetherThreatLevel.Medium,
            PeerThreatLevel.High     => AetherThreatLevel.High,
            PeerThreatLevel.Critical => AetherThreatLevel.Critical,
            _                        => AetherThreatLevel.None,
        };

    // ─── PeerDirectiveKind → SecurityDirectiveKind ───────────────────────────

    internal static SecurityDirectiveKind ToSecurityDirectiveKind(PeerDirectiveKind kind) =>
        kind switch
        {
            PeerDirectiveKind.ElevateMonitoring => SecurityDirectiveKind.ElevateMonitoring,
            PeerDirectiveKind.AvoidNode         => SecurityDirectiveKind.AvoidNode,
            PeerDirectiveKind.QuarantineNode    => SecurityDirectiveKind.QuarantineNode,
            PeerDirectiveKind.ReleaseNode       => SecurityDirectiveKind.ReleaseNode,
            _                                   => SecurityDirectiveKind.ElevateMonitoring,
        };
}
