using System.Collections.Generic;

namespace Circle.AI.Tools
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

        /// <summary>Convenience factory for a failed tool result.</summary>
        public static ToolResult Failure(string toolName, string error) =>
            new() { ToolName = toolName, Success = false, Error = error };

        /// <summary>Convenience factory for a successful tool result.</summary>
        public static ToolResult Ok(string toolName, object? result = null) =>
            new() { ToolName = toolName, Success = true, Result = result };
    }
}
