using System.Collections.Generic;

namespace Bhengu.AI.Tools
{
    /// <summary>
    /// Describes a tool the model can call. Compatible with OpenAI/Qwen function-call schema.
    /// </summary>
    public sealed class ToolDefinition
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required IReadOnlyDictionary<string, ToolParameter> Parameters { get; init; }
        public required IReadOnlyList<string> RequiredParameters { get; init; }
    }

    public sealed class ToolParameter
    {
        public required string Type { get; init; }      // "string", "number", "boolean", "object", "array"
        public required string Description { get; init; }
        public string[]? Enum { get; init; }
    }

    public sealed class ToolInvocation
    {
        public required string ToolName { get; init; }
        public required IReadOnlyDictionary<string, object?> Arguments { get; init; }
    }

    public sealed class ToolResult
    {
        public required string ToolName { get; init; }
        public required bool Success { get; init; }
        public object? Result { get; init; }
        public string? Error { get; init; }
    }
}
