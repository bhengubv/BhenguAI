// ToolDefinitionBuilder.cs
//
// Fluent builder for constructing ToolDefinition instances. Accumulates
// parameters in lists and builds immutable dictionaries on Build().

using System;
using System.Collections.Generic;

namespace Bhengu.AI.Tools;

/// <summary>
/// Fluent builder for constructing <see cref="ToolDefinition"/> instances.
/// </summary>
/// <example>
/// <code>
/// var tool = ToolDefinitionBuilder.Create("get_weather")
///     .Description("Get current weather for a location")
///     .Parameter("city", "string", "The city name", required: true)
///     .Parameter("units", "string", "Temperature units", required: false,
///         enumValues: new[] { "celsius", "fahrenheit" })
///     .Build();
/// </code>
/// </example>
public sealed class ToolDefinitionBuilder
{
    private readonly string _name;
    private string? _description;
    private readonly List<(string Name, ToolParameter Parameter, bool Required)> _parameters = [];

    private ToolDefinitionBuilder(string name)
    {
        _name = name;
    }

    /// <summary>
    /// Creates a new builder for a tool with the given <paramref name="name"/>.
    /// </summary>
    /// <param name="name">
    /// The tool name. Must be non-null and non-empty. Typically a snake_case
    /// identifier matching the function-call schema (e.g. <c>"get_weather"</c>).
    /// </param>
    /// <returns>A new <see cref="ToolDefinitionBuilder"/> instance.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is <c>null</c> or empty.
    /// </exception>
    public static ToolDefinitionBuilder Create(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new ToolDefinitionBuilder(name);
    }

    /// <summary>
    /// Sets the human-readable description for the tool.
    /// </summary>
    /// <param name="description">
    /// A concise description of what the tool does. Must be non-null and non-empty.
    /// </param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="description"/> is <c>null</c> or empty.
    /// </exception>
    public ToolDefinitionBuilder Description(string description)
    {
        ArgumentException.ThrowIfNullOrEmpty(description);
        _description = description;
        return this;
    }

    /// <summary>
    /// Adds a parameter to the tool definition.
    /// </summary>
    /// <param name="name">The parameter name. Must be non-null and non-empty.</param>
    /// <param name="type">
    /// The JSON Schema type: <c>"string"</c>, <c>"number"</c>, <c>"boolean"</c>,
    /// <c>"object"</c>, or <c>"array"</c>.
    /// </param>
    /// <param name="description">A human-readable description of the parameter.</param>
    /// <param name="required">
    /// When <c>true</c>, the parameter is added to the required list. Default <c>false</c>.
    /// </param>
    /// <param name="enumValues">
    /// Optional set of allowed values (for string-typed parameters). Default <c>null</c>.
    /// </param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/>, <paramref name="type"/>, or
    /// <paramref name="description"/> is <c>null</c> or empty.
    /// </exception>
    public ToolDefinitionBuilder Parameter(
        string name,
        string type,
        string description,
        bool required = false,
        string[]? enumValues = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentException.ThrowIfNullOrEmpty(description);

        var param = new ToolParameter
        {
            Type = type,
            Description = description,
            Enum = enumValues,
        };

        _parameters.Add((name, param, required));
        return this;
    }

    /// <summary>
    /// Builds the final <see cref="ToolDefinition"/> from the accumulated state.
    /// </summary>
    /// <returns>An immutable <see cref="ToolDefinition"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Description(string)"/> was not called before <see cref="Build"/>.
    /// </exception>
    public ToolDefinition Build()
    {
        if (string.IsNullOrEmpty(_description))
            throw new InvalidOperationException(
                $"ToolDefinition '{_name}' requires a description. Call Description() before Build().");

        var parameters = new Dictionary<string, ToolParameter>(_parameters.Count);
        var required = new List<string>();

        foreach (var (name, param, isRequired) in _parameters)
        {
            parameters[name] = param;
            if (isRequired)
                required.Add(name);
        }

        return new ToolDefinition
        {
            Name = _name,
            Description = _description,
            Parameters = parameters,
            RequiredParameters = required,
        };
    }
}
