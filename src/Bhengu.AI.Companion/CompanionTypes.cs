// CompanionTypes.cs
//
// Core types for the Circle AI Companion layer.
// The Companion is the HER + JARVIS persona — available on every surface,
// with memory and identity that travels with the person.

namespace Bhengu.AI.Companion;

// ─────────────────────────────────────────────────────────────────────────────
// Enumerations
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The surface on which the Companion session is running.
/// Determines sensory capabilities, available UI affordances, and
/// how the Companion adapts its communication style.
/// </summary>
public enum InterfaceKind
{
    /// <summary>Mobile phone or tablet (MAUI).</summary>
    Mobile,

    /// <summary>Smartwatch or fitness band with a small display.</summary>
    Wearable,

    /// <summary>Desktop or laptop computer (MAUI or WPF).</summary>
    Desktop,

    /// <summary>Browser-based experience (Blazor).</summary>
    Web,

    /// <summary>Embedded IoT device — voice in, voice out, minimal compute.</summary>
    IoT,

    /// <summary>Always-on ambient surface — smart speaker, room display, car.</summary>
    Ambient,

    /// <summary>Programmatic / background / testing context (no UI).</summary>
    Headless
}

/// <summary>
/// Snapshot of all context injected into the Companion's system prompt.
/// Rebuilt at the start of each session and refreshed on request.
/// </summary>
public sealed record CompanionContext(
    string IdentityId,
    string DisplayName,
    string? PreferredLanguage,
    InterfaceKind Interface,
    string PersonaHints,
    string AffectSummary,
    IReadOnlyList<string> RecentMemorySnippets,
    IReadOnlyList<string> ActiveGoals,
    DateTimeOffset ContextBuiltAt
);

/// <summary>
/// A single turn in the Companion conversation log, held in memory for the
/// duration of the session.
/// </summary>
public sealed record CompanionTurn(
    string Role,          // "user" | "assistant"
    string Content,
    DateTimeOffset Timestamp
);

/// <summary>
/// Metadata emitted when the Companion proactively initiates contact.
/// Mirrors <see cref="Bhengu.AI.Hosting.ProactiveMessageEventArgs"/> but
/// enriched with Companion session info.
/// </summary>
public sealed record CompanionProactiveEvent(
    string SessionId,
    string IdentityId,
    InterfaceKind Interface,
    string Message,
    string TriggerName,
    DateTimeOffset GeneratedAt
);
