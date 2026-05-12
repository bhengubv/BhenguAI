namespace Bhengu.AI.Aether;

/// <summary>
/// Categories of security-relevant observations Aether can detect at the
/// protocol layer, without requiring AI. The AI Security Layer consumes
/// these events to produce threat assessments and directives.
/// </summary>
public enum AetherSecurityEventKind
{
    /// <summary>A node attempted to authenticate into the mesh.</summary>
    NodeAuthAttempt,

    /// <summary>Traffic was observed deviating from expected routing paths.</summary>
    RoutingAnomaly,

    /// <summary>A node's behaviour deviated from its established baseline.</summary>
    NodeBehaviourChange,

    /// <summary>A key exchange or certificate validation event occurred.</summary>
    EncryptionEvent,

    /// <summary>Active attack signature detected (e.g. replay, spoofing).</summary>
    IntrusionSignal,

    /// <summary>A node requested capabilities beyond its granted level.</summary>
    PrivilegeAttempt,
}

/// <summary>
/// Protocol-level threat severity as assessed by Aether itself, before any
/// AI reasoning is applied.
/// </summary>
public enum AetherThreatLevel
{
    None,
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>
/// Emitted by Aether when a security-relevant event occurs at the protocol
/// layer. This is the primary feed for the AI Security Layer.
/// Aether never calls into BhenguAI — it only emits; BhenguAI subscribes.
/// </summary>
public sealed record AetherSecurityEvent(
    string NodeId,
    AetherSecurityEventKind Kind,
    AetherThreatLevel ThreatLevel,
    string Description,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset OccurredAt)
{
    /// <summary>True when ThreatLevel is High or Critical.</summary>
    public bool IsHighSeverity =>
        ThreatLevel is AetherThreatLevel.High or AetherThreatLevel.Critical;
}
