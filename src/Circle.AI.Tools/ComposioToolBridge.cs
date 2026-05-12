// ComposioToolBridge.cs
//
// Routes B! tool calls to a Composio MCP server via JSON-RPC 2.0 over HTTP.
// Composio provides 250+ integrations (Gmail, Slack, GitHub, Calendar, etc.)
// through a single MCP endpoint.
// See: https://composio.dev/mcp

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Circle.AI.Tools
{
    /// <summary>
    /// Routes tool calls to a Composio MCP server via JSON-RPC 2.0 over HTTP.
    /// Composio provides 250+ integrations (Gmail, Slack, GitHub, Calendar, etc.)
    /// through a single endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bridge sends every tool invocation as a <c>tools/call</c> JSON-RPC 2.0
    /// request and interprets the response envelope to produce a <see cref="ToolResult"/>.
    /// </para>
    /// <para>
    /// Tool discovery calls <c>GET {serverUri}/tools</c> and maps each returned entry
    /// to a <see cref="ToolDefinition"/>.
    /// </para>
    /// </remarks>
    public sealed class ComposioToolBridge : IToolBridge, IDisposable, IAsyncDisposable
    {
        private static readonly Uri DefaultServerUri = new("https://mcp.composio.dev/", UriKind.Absolute);

        private readonly string _apiKey;
        private readonly Uri _serverUri;
        private readonly HttpClient _http;
        private readonly bool _ownsHttpClient;

        // JSON options shared across all serialisation calls.
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>
        /// Initialises the bridge.
        /// </summary>
        /// <param name="composioApiKey">Composio API key sent in the <c>X-API-Key</c> header.</param>
        /// <param name="serverUri">
        /// Base URI of the Composio MCP endpoint.
        /// Defaults to <c>https://mcp.composio.dev/</c>.
        /// </param>
        /// <param name="httpClient">
        /// Optional pre-configured <see cref="HttpClient"/>. When <c>null</c> a new instance
        /// is created and owned by this bridge (disposed in <see cref="DisposeAsync"/>).
        /// </param>
        public ComposioToolBridge(
            string composioApiKey,
            Uri? serverUri = null,
            HttpClient? httpClient = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(composioApiKey);

            _apiKey = composioApiKey;
            _serverUri = EnsureTrailingSlash(serverUri ?? DefaultServerUri);

            if (httpClient is not null)
            {
                _http = httpClient;
                _ownsHttpClient = false;
            }
            else
            {
                _http = new HttpClient();
                _ownsHttpClient = true;
            }

            _http.DefaultRequestHeaders.Accept.Clear();
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        // ------------------------------------------------------------------
        // IToolBridge — synchronous tool list (empty until discovery is run)
        // ------------------------------------------------------------------

        /// <summary>
        /// Synchronous available-tools list. Empty by default; call
        /// <see cref="GetAvailableToolsAsync"/> to populate via the Composio API.
        /// </summary>
        public IReadOnlyList<ToolDefinition> AvailableTools { get; private set; } =
            Array.Empty<ToolDefinition>();

        // ------------------------------------------------------------------
        // IToolBridge — invoke
        // ------------------------------------------------------------------

        /// <summary>
        /// Invokes a tool on the Composio MCP server via JSON-RPC 2.0.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="invocation"/> has a null or empty
        /// <see cref="ToolInvocation.ToolName"/>.
        /// </exception>
        public async Task<ToolResult> InvokeAsync(
            ToolInvocation invocation,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(invocation);
            if (string.IsNullOrWhiteSpace(invocation.ToolName))
                throw new ArgumentException("ToolName must not be null or whitespace.", nameof(invocation));

            var requestBody = new JsonRpcRequest
            {
                Method = "tools/call",
                Id     = 1,
                Params = new ToolCallParams
                {
                    Name      = invocation.ToolName,
                    Arguments = invocation.Arguments,
                },
            };

            var endpoint = new Uri(_serverUri, $"tools/{Uri.EscapeDataString(invocation.ToolName)}/invoke");

            try
            {
                using var request = BuildRequest(HttpMethod.Post, endpoint, requestBody);
                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

                var body = await response.Content
                    .ReadFromJsonAsync<JsonElement>(JsonOptions, ct)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var httpError = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                    return ToolResult.Failure(invocation.ToolName, ExtractError(body, httpError));
                }

                // Standard JSON-RPC 2.0 response: { "result": ..., "error": ... }
                if (body.TryGetProperty("error", out var errNode) &&
                    errNode.ValueKind != JsonValueKind.Null)
                {
                    var msg = errNode.TryGetProperty("message", out var m)
                        ? m.GetString() ?? errNode.ToString()
                        : errNode.ToString();
                    return ToolResult.Failure(invocation.ToolName, msg);
                }

                if (body.TryGetProperty("result", out var resultNode))
                    return ToolResult.Ok(invocation.ToolName, resultNode);

                // No result / error — treat as success with null payload.
                return ToolResult.Ok(invocation.ToolName);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ToolResult.Failure(invocation.ToolName, ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // IToolBridge — discovery
        // ------------------------------------------------------------------

        /// <summary>
        /// Fetches the list of tools available on the Composio MCP server
        /// (<c>GET {serverUri}/tools</c>) and caches it in <see cref="AvailableTools"/>.
        /// </summary>
        public async Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(
            CancellationToken ct = default)
        {
            var endpoint = new Uri(_serverUri, "tools");

            try
            {
                using var request = BuildRequest(HttpMethod.Get, endpoint, body: null);
                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return Array.Empty<ToolDefinition>();

                var root = await response.Content
                    .ReadFromJsonAsync<JsonElement>(JsonOptions, ct)
                    .ConfigureAwait(false);

                var tools = ParseToolList(root);
                AvailableTools = tools;
                return tools;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return Array.Empty<ToolDefinition>();
            }
        }

        // ------------------------------------------------------------------
        // IAsyncDisposable
        // ------------------------------------------------------------------

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_ownsHttpClient)
                _http.Dispose();
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        // ------------------------------------------------------------------
        // Internal helpers
        // ------------------------------------------------------------------

        private HttpRequestMessage BuildRequest(HttpMethod method, Uri uri, object? body)
        {
            var req = new HttpRequestMessage(method, uri);
            req.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);

            if (body is not null)
                req.Content = JsonContent.Create(body, options: JsonOptions);

            return req;
        }

        private static IReadOnlyList<ToolDefinition> ParseToolList(JsonElement root)
        {
            // Composio may return an array at root, or { "tools": [...] }.
            JsonElement toolsArray;
            if (root.ValueKind == JsonValueKind.Array)
            {
                toolsArray = root;
            }
            else if (root.TryGetProperty("tools", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                toolsArray = arr;
            }
            else
            {
                return Array.Empty<ToolDefinition>();
            }

            var result = new List<ToolDefinition>(toolsArray.GetArrayLength());
            foreach (var item in toolsArray.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                var desc = item.TryGetProperty("description", out var d) ? d.GetString() : string.Empty;

                if (string.IsNullOrWhiteSpace(name)) continue;

                var parameters = new Dictionary<string, ToolParameter>(StringComparer.Ordinal);
                var required   = new List<string>();

                if (item.TryGetProperty("inputSchema", out var schema) &&
                    schema.TryGetProperty("properties", out var props) &&
                    props.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in props.EnumerateObject())
                    {
                        var type = prop.Value.TryGetProperty("type", out var t)
                            ? t.GetString() ?? "string"
                            : "string";
                        var propDesc = prop.Value.TryGetProperty("description", out var pd)
                            ? pd.GetString() ?? string.Empty
                            : string.Empty;

                        parameters[prop.Name] = new ToolParameter { Type = type, Description = propDesc };
                    }

                    if (schema.TryGetProperty("required", out var req) &&
                        req.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var r in req.EnumerateArray())
                        {
                            var rName = r.GetString();
                            if (!string.IsNullOrWhiteSpace(rName)) required.Add(rName);
                        }
                    }
                }

                result.Add(new ToolDefinition
                {
                    Name               = name!,
                    Description        = desc ?? string.Empty,
                    Parameters         = parameters,
                    RequiredParameters = required,
                });
            }

            return result;
        }

        private static string ExtractError(JsonElement body, string fallback)
        {
            if (body.TryGetProperty("error", out var e))
            {
                if (e.TryGetProperty("message", out var m))
                    return m.GetString() ?? fallback;
                return e.ToString();
            }
            return fallback;
        }

        private static Uri EnsureTrailingSlash(Uri uri)
        {
            var s = uri.ToString();
            return s.EndsWith('/') ? uri : new Uri(s + "/", UriKind.Absolute);
        }

        // ------------------------------------------------------------------
        // Private DTO types (JSON-RPC 2.0)
        // ------------------------------------------------------------------

        private sealed class JsonRpcRequest
        {
            [JsonPropertyName("jsonrpc")]
            public string JsonRpc { get; } = "2.0";

            [JsonPropertyName("method")]
            public required string Method { get; init; }

            [JsonPropertyName("id")]
            public required int Id { get; init; }

            [JsonPropertyName("params")]
            public required object Params { get; init; }
        }

        private sealed class ToolCallParams
        {
            [JsonPropertyName("name")]
            public required string Name { get; init; }

            [JsonPropertyName("arguments")]
            public required IReadOnlyDictionary<string, object?> Arguments { get; init; }
        }
    }
}
