// ICompanionSession.cs
//
// Primary contract for a Circle AI Companion session.
// A session is scoped to one identity on one interface surface.
// Memory, persona, and affect flow through this session and are
// synchronised across all other active sessions for the same identity.

using System.Runtime.CompilerServices;

namespace Bhengu.AI.Companion;

/// <summary>
/// A Companion conversation session. Combines identity awareness, cross-device
/// memory, language adaptation, affect sensing, and proactive reasoning into a
/// single coherent interface.
/// </summary>
public interface ICompanionSession : IAsyncDisposable
{
    // ── Identity ──────────────────────────────────────────────────────────

    /// <summary>Stable unique identifier for this session.</summary>
    string SessionId { get; }

    /// <summary>The authenticated identity driving this session.</summary>
    string IdentityId { get; }

    /// <summary>The surface on which this session is running.</summary>
    InterfaceKind Interface { get; }

    // ── Core conversation ─────────────────────────────────────────────────

    /// <summary>
    /// Send a message to the Companion and receive a complete reply.
    /// Context enrichment (identity, memory, persona, affect, language) is
    /// applied automatically.
    /// </summary>
    Task<string> SendAsync(string message, CancellationToken ct = default);

    /// <summary>
    /// Stream the Companion's reply token-by-token for low-latency rendering.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        string message,
        [EnumeratorCancellation] CancellationToken ct = default);

    /// <summary>
    /// Agentic mode: sends the instruction, detects tool calls in the reply,
    /// executes them, and re-prompts until the model produces a plain-text
    /// answer. Enables "do things, not just say things."
    /// </summary>
    Task<string> AgentAsync(string instruction, CancellationToken ct = default);

    // ── Context ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the most recent <see cref="CompanionContext"/> snapshot,
    /// including identity, persona hints, affect summary, and recent memories.
    /// </summary>
    CompanionContext GetContext();

    /// <summary>
    /// Refreshes the context from backing stores (memory, persona, affect).
    /// Call after significant state changes (e.g. new goal set, mood shift).
    /// </summary>
    Task RefreshContextAsync(CancellationToken ct = default);

    // ── History ───────────────────────────────────────────────────────────

    /// <summary>
    /// The in-session conversation history (this session only, not persisted).
    /// </summary>
    IReadOnlyList<CompanionTurn> History { get; }

    // ── Feedback ──────────────────────────────────────────────────────────

    /// <summary>
    /// Signal satisfaction with the last reply. Used to evolve the persona
    /// and communication style over time.
    /// </summary>
    Task SignalFeedbackAsync(
        bool positive, string? note = null, CancellationToken ct = default);

    // ── Proactive ────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when the Companion initiates contact without being prompted —
    /// e.g. a goal check-in, a mood-triggered nudge, or a scheduled reminder.
    /// </summary>
    event EventHandler<CompanionProactiveEvent>? ProactiveMessageReady;
}
