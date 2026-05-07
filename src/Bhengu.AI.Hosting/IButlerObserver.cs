// IButlerObserver.cs
//
// Neutral observability hook. Consumers of this interface receive lifecycle
// and inference events from the butler service WITHOUT any Karma / Qi logic
// baked in. Platform-specific accumulation logic (e.g. TheGeekNetwork's
// private repo) wires its own implementation here — BhenguAI stays clean.
//
// All methods have default no-op implementations so partial observers are
// trivial to write. Observer exceptions are caught by ButlerService and
// logged; they never propagate to the caller.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bhengu.AI.Inference;
using Bhengu.AI.Tools;

namespace Bhengu.AI.Hosting;

// ---------------------------------------------------------------------------
// Event records
// ---------------------------------------------------------------------------

/// <summary>
/// Payload delivered to <see cref="IButlerObserver.OnChatCompletedAsync"/>.
/// Carries the full conversation and the model's reply.
/// </summary>
/// <param name="CorrelationId">Per-call GUID for end-to-end tracing.</param>
/// <param name="Messages">The input messages passed to the generator.</param>
/// <param name="Response">The complete response text.</param>
/// <param name="Elapsed">Wall-clock time from first token to last token.</param>
/// <param name="Timestamp">UTC moment the call completed.</param>
public sealed record ButlerChatEvent(
    Guid CorrelationId,
    IReadOnlyList<ChatMessage> Messages,
    string Response,
    TimeSpan Elapsed,
    DateTimeOffset Timestamp);

/// <summary>
/// Payload delivered to <see cref="IButlerObserver.OnStreamStartedAsync"/> and
/// <see cref="IButlerObserver.OnStreamCompletedAsync"/>.
/// </summary>
/// <param name="CorrelationId">Per-call GUID for end-to-end tracing.</param>
/// <param name="Messages">The input messages passed to the generator.</param>
/// <param name="Elapsed">
/// For <c>OnStreamStarted</c>: time-to-first-token.<br/>
/// For <c>OnStreamCompleted</c>: total generation time.
/// </param>
/// <param name="TokenCount">
/// For <c>OnStreamStarted</c>: <c>0</c>.<br/>
/// For <c>OnStreamCompleted</c>: number of tokens yielded.
/// </param>
/// <param name="Timestamp">UTC moment of the event.</param>
public sealed record ButlerStreamEvent(
    Guid CorrelationId,
    IReadOnlyList<ChatMessage> Messages,
    TimeSpan Elapsed,
    int TokenCount,
    DateTimeOffset Timestamp);

/// <summary>
/// Payload delivered to <see cref="IButlerObserver.OnToolInvokedAsync"/>.
/// </summary>
/// <param name="CorrelationId">Per-call GUID for end-to-end tracing.</param>
/// <param name="Invocation">The tool call that was dispatched.</param>
/// <param name="Result">The result returned by the tool bridge.</param>
/// <param name="Elapsed">Wall-clock time for the tool call.</param>
/// <param name="Timestamp">UTC moment the call completed.</param>
public sealed record ButlerToolEvent(
    Guid CorrelationId,
    ToolInvocation Invocation,
    ToolResult Result,
    TimeSpan Elapsed,
    DateTimeOffset Timestamp);

// ---------------------------------------------------------------------------
// Interface
// ---------------------------------------------------------------------------

/// <summary>
/// Observability hook for <see cref="ButlerService"/>. Receives lifecycle and
/// inference events. All methods are optional (default = no-op) and must
/// complete quickly — long-running work should be dispatched to a background
/// channel inside the implementation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread safety:</b> Methods may be called concurrently. Implementations
/// must be thread-safe.
/// </para>
/// <para>
/// <b>Error isolation:</b> Exceptions thrown by observer methods are caught
/// by <see cref="ButlerService"/> and logged. They never propagate to the
/// caller of the butler.
/// </para>
/// <para>
/// <b>Intended extension point:</b> Platform consumers (e.g. TheGeekNetwork)
/// implement this interface to track usage, accumulate Qi/Karma, or feed
/// analytics — all without modifying BhenguAI source.
/// </para>
/// </remarks>
public interface IButlerObserver
{
    /// <summary>Called once after the model has loaded and Butler is ready.</summary>
    ValueTask OnStartedAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <summary>Called once when Butler is stopping / being disposed.</summary>
    ValueTask OnStoppedAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <summary>
    /// Called after a complete (non-streaming) chat response has been generated
    /// and returned to the caller.
    /// </summary>
    ValueTask OnChatCompletedAsync(ButlerChatEvent @event, CancellationToken ct = default)
        => ValueTask.CompletedTask;

    /// <summary>
    /// Called when a streaming response emits its first token.
    /// <see cref="ButlerStreamEvent.TokenCount"/> is <c>0</c> at this point.
    /// </summary>
    ValueTask OnStreamStartedAsync(ButlerStreamEvent @event, CancellationToken ct = default)
        => ValueTask.CompletedTask;

    /// <summary>
    /// Called after a streaming response has finished (all tokens yielded, or
    /// the stream was cancelled). <see cref="ButlerStreamEvent.TokenCount"/>
    /// holds the number of tokens that were emitted before completion.
    /// </summary>
    ValueTask OnStreamCompletedAsync(ButlerStreamEvent @event, CancellationToken ct = default)
        => ValueTask.CompletedTask;

    /// <summary>
    /// Called after a tool invocation has completed (success or failure).
    /// Check <see cref="ToolResult.Success"/> inside <see cref="ButlerToolEvent.Result"/>.
    /// </summary>
    ValueTask OnToolInvokedAsync(ButlerToolEvent @event, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}
