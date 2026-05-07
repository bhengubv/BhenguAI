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
    }
}
