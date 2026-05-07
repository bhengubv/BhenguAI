// IButlerService.cs
//
// The single contract for a long-lived B! butler process. The service holds
// the loaded model in memory for the process lifetime so that callers don't
// pay the 8 GB load cost per request. Implementations are expected to be
// thread-safe — concurrent callers can share the same service instance.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Hosting;

/// <summary>
/// Long-lived butler service. Owns the loaded chat generator and exposes
/// ask / chat / stream / tool entry points to the rest of the host process
/// (and, via <see cref="IButlerEndpoint"/>, to other processes).
/// </summary>
public interface IButlerService : IAsyncDisposable
{
    /// <summary>
    /// <c>true</c> once <see cref="StartAsync"/> has completed and the model
    /// is loaded. Calls made before this is set will block (or fail, depending
    /// on the implementation) until startup finishes.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Resolves the model file, loads it, and (optionally) runs a warm-up
    /// generation so the first user request doesn't pay the cold-start cost.
    /// Calling this multiple times is a no-op after the first success.
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Releases the model handle and shuts the service down. Subsequent calls
    /// to ask / chat / stream / tool will throw <see cref="ObjectDisposedException"/>.
    /// </summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Convenience wrapper around <see cref="ChatAsync"/> for a single user
    /// question. The configured system prompt is prepended automatically.
    /// </summary>
    Task<string> AskAsync(string question, CancellationToken ct = default);

    /// <summary>
    /// Generates a complete assistant reply for the supplied conversation.
    /// The configured system prompt is prepended if no system message is
    /// present in <paramref name="messages"/>.
    /// </summary>
    Task<string> ChatAsync(
        IReadOnlyList<Bhengu.AI.Inference.ChatMessage> messages,
        Bhengu.AI.Inference.GenerationOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Streams the assistant reply token-by-token. Each yielded string is the
    /// next chunk to append to the output — callers should concatenate them
    /// in order.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<Bhengu.AI.Inference.ChatMessage> messages,
        Bhengu.AI.Inference.GenerationOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Routes a tool invocation to the configured <see cref="Bhengu.AI.Tools.IToolBridge"/>.
    /// Returns a failure <see cref="Bhengu.AI.Tools.ToolResult"/> if no bridge
    /// is wired up.
    /// </summary>
    Task<Bhengu.AI.Tools.ToolResult> InvokeToolAsync(
        Bhengu.AI.Tools.ToolInvocation invocation,
        CancellationToken ct = default);
}
