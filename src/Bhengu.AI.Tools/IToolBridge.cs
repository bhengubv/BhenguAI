using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Tools
{
    /// <summary>
    /// Bridge between the local LLM and the TheGeekNetwork APIs. Implementations
    /// route tool calls to the appropriate API client (HTTP, in-process service, etc.).
    /// </summary>
    public interface IToolBridge
    {
        IReadOnlyList<ToolDefinition> AvailableTools { get; }
        Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken ct = default);

        /// <summary>
        /// Returns the tools available through this bridge by querying the remote
        /// service. Optional — implementations that expose a static tool list may
        /// return an empty list or the same value as <see cref="AvailableTools"/>.
        /// The default implementation returns the synchronous <see cref="AvailableTools"/> list.
        /// </summary>
        Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(CancellationToken ct = default)
            => Task.FromResult(AvailableTools);
    }
}
