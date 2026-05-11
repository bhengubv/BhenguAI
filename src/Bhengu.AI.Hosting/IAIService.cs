// IAIService.cs
//
// The single contract for a long-lived B! butler process. The service holds
// the loaded model in memory for the process lifetime so that callers don't
// pay the 8 GB load cost per request. Implementations are expected to be
// thread-safe — concurrent callers can share the same service instance.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bhengu.AI.Memory;

namespace Bhengu.AI.Hosting;

/// <summary>
/// Long-lived butler service. Owns the loaded chat generator and exposes
/// ask / chat / stream / tool / agentic entry points.
/// </summary>
public interface IAIService : IAsyncDisposable
{
    /// <summary>
    /// <c>true</c> once <see cref="StartAsync"/> has completed and the model
    /// is loaded.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Resolves the model file, loads it, and (optionally) runs a warm-up
    /// generation. Calling this multiple times is a no-op after the first
    /// success.
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Releases the model handle and shuts the service down.</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Convenience wrapper for a single user question. The configured system
    /// prompt (including device context, RAG snippets, and persona hints) is
    /// prepended automatically.
    /// </summary>
    Task<string> AskAsync(string question, CancellationToken ct = default);

    /// <summary>
    /// Generates a complete assistant reply for the supplied conversation.
    /// Context enrichment (device context, RAG, persona) is injected into
    /// the system message automatically.
    /// </summary>
    Task<string> ChatAsync(
        IReadOnlyList<Bhengu.AI.Inference.ChatMessage> messages,
        Bhengu.AI.Inference.GenerationOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Streams the assistant reply token-by-token. Context enrichment is
    /// applied exactly as in <see cref="ChatAsync"/>.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<Bhengu.AI.Inference.ChatMessage> messages,
        Bhengu.AI.Inference.GenerationOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Routes a tool invocation to the configured
    /// <see cref="Bhengu.AI.Tools.IToolBridge"/>. Returns a failure result
    /// when no bridge is wired up.
    /// </summary>
    Task<Bhengu.AI.Tools.ToolResult> InvokeToolAsync(
        Bhengu.AI.Tools.ToolInvocation invocation,
        CancellationToken ct = default);

    // ------------------------------------------------------------------
    // v2.0 additions
    // ------------------------------------------------------------------

    /// <summary>
    /// Agentic run: generates a response, detects embedded tool calls,
    /// executes them, and re-prompts — repeating until the model produces
    /// a plain text response or <see cref="AIOptions.AgenticMaxIterations"/>
    /// is reached.
    /// </summary>
    /// <param name="prompt">The user's request.</param>
    /// <param name="options">Optional generation knobs.</param>
    Task<string> AgenticChatAsync(
        string prompt,
        Bhengu.AI.Inference.GenerationOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Records a <see cref="FeedbackSignal"/> from the user against a past
    /// B! response. Stored in <see cref="AIOptions.FeedbackStore"/> and
    /// used to evolve the persona over time.
    /// </summary>
    Task SubmitFeedbackAsync(FeedbackSignal signal, CancellationToken ct = default);
}
