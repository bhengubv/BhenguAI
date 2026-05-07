using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Bhengu.AI.Tools
{
    /// <summary>
    /// Renders <see cref="ToolDefinition"/> collections into formats consumable by LLMs:
    /// - JSON in OpenAI/Qwen function-calling format (for tool_choice / tools fields).
    /// - Markdown for inclusion in a system prompt as documentation.
    /// </summary>
    public static class ToolManifestGenerator
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Renders the given tools as a JSON array in OpenAI/Qwen function-calling format.
        /// Each element is { "type": "function", "function": { "name", "description", "parameters": { ... } } }.
        /// </summary>
        public static string GenerateJsonManifest(IReadOnlyList<ToolDefinition> tools)
        {
            ArgumentNullException.ThrowIfNull(tools);

            var array = new List<Dictionary<string, object>>(tools.Count);

            foreach (var tool in tools)
            {
                var properties = new Dictionary<string, object>(tool.Parameters.Count);
                foreach (var kvp in tool.Parameters)
                {
                    var prop = new Dictionary<string, object>
                    {
                        ["type"] = kvp.Value.Type,
                        ["description"] = kvp.Value.Description
                    };
                    if (kvp.Value.Enum is { Length: > 0 })
                    {
                        prop["enum"] = kvp.Value.Enum;
                    }
                    properties[kvp.Key] = prop;
                }

                var parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = tool.RequiredParameters
                };

                array.Add(new Dictionary<string, object>
                {
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object>
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = parameters
                    }
                });
            }

            return JsonSerializer.Serialize(array, JsonOptions);
        }

        /// <summary>
        /// Renders the given tools as a human-readable Markdown summary, suitable for
        /// inclusion in a system prompt. Tools are grouped by API (the first segment
        /// after the "tgn." prefix).
        /// </summary>
        public static string GenerateMarkdownManifest(IReadOnlyList<ToolDefinition> tools)
        {
            ArgumentNullException.ThrowIfNull(tools);

            var sb = new StringBuilder();
            sb.AppendLine("# Available Tools");
            sb.AppendLine();
            sb.AppendLine($"Total: {tools.Count} tools.");
            sb.AppendLine();

            var groups = new SortedDictionary<string, List<ToolDefinition>>(StringComparer.Ordinal);
            foreach (var tool in tools)
            {
                var groupKey = ExtractApiSlug(tool.Name);
                if (!groups.TryGetValue(groupKey, out var list))
                {
                    list = new List<ToolDefinition>();
                    groups[groupKey] = list;
                }
                list.Add(tool);
            }

            foreach (var group in groups)
            {
                sb.AppendLine($"## {group.Key}");
                sb.AppendLine();
                foreach (var tool in group.Value)
                {
                    sb.AppendLine($"### `{tool.Name}`");
                    sb.AppendLine();
                    sb.AppendLine(tool.Description);
                    sb.AppendLine();

                    if (tool.Parameters.Count == 0)
                    {
                        sb.AppendLine("_No parameters._");
                        sb.AppendLine();
                        continue;
                    }

                    sb.AppendLine("Parameters:");
                    sb.AppendLine();
                    sb.AppendLine("| Name | Type | Required | Description |");
                    sb.AppendLine("|------|------|----------|-------------|");

                    var requiredSet = new HashSet<string>(tool.RequiredParameters, StringComparer.Ordinal);
                    foreach (var kvp in tool.Parameters)
                    {
                        var required = requiredSet.Contains(kvp.Key) ? "yes" : "no";
                        var desc = EscapePipe(kvp.Value.Description);
                        if (kvp.Value.Enum is { Length: > 0 })
                        {
                            desc += " Allowed values: " + string.Join(", ", kvp.Value.Enum) + ".";
                        }
                        sb.AppendLine($"| `{kvp.Key}` | {kvp.Value.Type} | {required} | {desc} |");
                    }
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private static string ExtractApiSlug(string toolName)
        {
            // Tool names are "tgn.<api>.<verb>". Group by "tgn.<api>".
            const string prefix = "tgn.";
            if (!toolName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return toolName;
            }

            var rest = toolName.Substring(prefix.Length);
            var dot = rest.IndexOf('.');
            return dot < 0 ? prefix + rest : prefix + rest.Substring(0, dot);
        }

        private static string EscapePipe(string s) => s.Replace("|", "\\|", StringComparison.Ordinal);
    }
}
