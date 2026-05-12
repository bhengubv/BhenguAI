// AIApiClient.cs
//
// IAIService implementation that proxies every call to the remote
// ButlerAPI (port 5170 on thegeeknetwork.co.za, or a developer override).
// Used as the cloud fallback when the device either:
//   (a) has not yet downloaded a local model, or
//   (b) has insufficient RAM to run local inference.
//
// Wire-up (via FallbackAIService — preferred) or standalone:
//
//   var client = new AIApiClient(
//       new Uri("https://butler.thegeeknetwork.co.za"),
//       bearerToken: options.CloudFallbackToken);

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Circle.AI.Inference;
using Circle.AI.Memory;
using Circle.AI.Tools;

namespace Circle.AI.Hosting;

/// <summary>
/// <see cref="IAIService"/> that proxies requests to a remote ButlerAPI
/// instance over HTTP/JSON. Streaming responses use Server-Sent Events (SSE).
/// </summary>
public sealed class AIApiClient : IAIService
{
    // ------------------------------------------------------------------
    // Wire format DTOs (kept private — callers use IAIService)
    // ------------------------------------------------------------------

    private sealed record AskRequest([property: JsonPropertyName("question")] string Question);

    private sealed record ChatRequest(
        [property: JsonPropertyName("messages")]  IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("options")]   GenerationOptions?         Options);

    private sealed record AgenticRequest(
        [property: JsonPropertyName("prompt")]  string            Prompt,
        [property: JsonPropertyName("options")] GenerationOptions? Options);

    private sealed record ToolRequest(
        [property: JsonPropertyName("name")]      string                                ToolName,
        [property: JsonPropertyName("arguments")] IReadOnlyDictionary<string, object?> Arguments);

    private sealed record FeedbackRequest(
        [property: JsonPropertyName("id")]            Guid             Id,
        [property: JsonPropertyName("polarity")]      FeedbackPolarity Polarity,
        [property: JsonPropertyName("userText")]      string           UserText,
        [property: JsonPropertyName("assistantText")] string           AssistantText,
        [property: JsonPropertyName("comment")]       string?          Comment);

    private sealed record StringPayload([property: JsonPropertyName("text")] string Text);

    // ------------------------------------------------------------------
    // Fields
    // ------------------------------------------------------------------

    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private bool _disposed;

    // ------------------------------------------------------------------
    // IAIService.IsReady — true once a /health check succeeds
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public bool IsReady { get; private set; }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    /// <param name="endpoint">Base URI of the ButlerAPI, e.g. <c>https://butler.thegeeknetwork.co.za</c>.</param>
    /// <param name="bearerToken">Optional bearer token sent in every request.</param>
    /// <param name="httpClient">Optional pre-configured <see cref="HttpClient"/>. When <c>null</c> a default is created.</param>
    public AIApiClient(Uri endpoint, string? bearerToken = null, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        _http = httpClient ?? new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromMinutes(5) };
        _http.BaseAddress ??= endpoint;

        if (!string.IsNullOrWhiteSpace(bearerToken))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", bearerToken);
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    /// <inheritdoc />
    /// <remarks>Performs a <c>GET /api/butler/health</c> to confirm the remote is ready.</remarks>
    public async Task StartAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("api/butler/health", ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        IsReady = true;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct = default)
    {
        IsReady = false;
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // Inference
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<string> AskAsync(string question, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("api/butler/ask", new AskRequest(question), _json, ct)
                               .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<StringPayload>(_json, ct).ConfigureAwait(false);
        return payload?.Text ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("api/butler/chat", new ChatRequest(messages, options), _json, ct)
                               .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<StringPayload>(_json, ct).ConfigureAwait(false);
        return payload?.Text ?? string.Empty;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses Server-Sent Events: each SSE line is <c>data: {token}</c>. The
    /// stream ends when the server sends <c>data: [DONE]</c>.
    /// </remarks>
    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body  = JsonSerializer.Serialize(new ChatRequest(messages, options), _json);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/butler/stream")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var token = line["data:".Length..].Trim();
            if (token == "[DONE]") yield break;
            if (!string.IsNullOrEmpty(token)) yield return token;
        }
    }

    /// <inheritdoc />
    public async Task<string> AgenticChatAsync(
        string prompt,
        GenerationOptions? options = null,
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync(
            "api/butler/agentic", new AgenticRequest(prompt, options), _json, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<StringPayload>(_json, ct).ConfigureAwait(false);
        return payload?.Text ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<ToolResult> InvokeToolAsync(
        ToolInvocation invocation,
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync(
            "api/butler/tool",
            new ToolRequest(invocation.ToolName, invocation.Arguments),
            _json, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ToolResult>(_json, ct).ConfigureAwait(false)
               ?? ToolResult.Failure(invocation.ToolName, "Empty response from cloud");
    }

    /// <inheritdoc />
    public async Task SubmitFeedbackAsync(FeedbackSignal signal, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync(
            "api/butler/feedback",
            new FeedbackRequest(signal.Id, signal.Polarity, signal.UserText, signal.AssistantText, signal.Comment),
            _json, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    // ------------------------------------------------------------------
    // IAsyncDisposable
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _http.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}
