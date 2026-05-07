// ButlerHttpClient.cs
//
// Out-of-process client for HttpLoopbackEndpoint. Mirrors the IButlerService
// surface so callers (e.g. CircleOS keyboard) can talk to a remote Butler
// running in the CircleOS background service with the same shape they'd use
// for an in-process service.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bhengu.AI.Inference;
using Bhengu.AI.Tools;

namespace Bhengu.AI.Hosting.Endpoints;

/// <summary>
/// HTTP client that talks to a <see cref="HttpLoopbackEndpoint"/>. Methods
/// mirror <see cref="IButlerService"/> so the same call sites work in-process
/// (direct service) or out-of-process (this client).
/// </summary>
public sealed class ButlerHttpClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    /// <summary>
    /// Connects to a loopback Butler endpoint at <c>http://127.0.0.1:{port}/</c>.
    /// </summary>
    /// <param name="port">Port the endpoint bound to (see <see cref="HttpLoopbackEndpoint.BoundPort"/>).</param>
    /// <param name="token">Shared-secret token (see <see cref="HttpLoopbackEndpoint.Token"/>).</param>
    public ButlerHttpClient(int port, string token)
        : this(BuildDefaultHttpClient(port, token), ownsClient: true)
    {
    }

    /// <summary>
    /// Wraps a pre-configured <see cref="HttpClient"/>. Useful when callers
    /// want to control timeouts, proxies, or share clients. The client must
    /// already have <c>BaseAddress</c> set to the endpoint root and the
    /// <c>X-Butler-Token</c> header configured.
    /// </summary>
    public ButlerHttpClient(HttpClient httpClient, bool ownsClient = false)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsClient = ownsClient;
    }

    /// <summary>Mirrors <see cref="IButlerService.AskAsync"/>.</summary>
    public async Task<string> AskAsync(string question, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(question);
        using var response = await _http.PostAsJsonAsync(
            "butler/ask",
            new AskPayload { Question = question },
            JsonOpts,
            ct).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Mirrors <see cref="IButlerService.ChatAsync"/>.</summary>
    public async Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var payload = new ChatPayload
        {
            Messages = ConvertMessages(messages),
            Options = options is null ? null : GenerationOptionsPayload.From(options),
        };

        using var response = await _http.PostAsJsonAsync("butler/chat", payload, JsonOpts, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var parsed = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOpts, ct).ConfigureAwait(false);
        return parsed?.Content ?? string.Empty;
    }

    /// <summary>Mirrors <see cref="IButlerService.StreamAsync"/>.</summary>
    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var payload = new ChatPayload
        {
            Messages = ConvertMessages(messages),
            Options = options is null ? null : GenerationOptionsPayload.From(options),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "butler/stream")
        {
            Content = JsonContent.Create(payload, options: JsonOpts),
        };

        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(body, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) yield break;        // server closed
            if (line.Length == 0) continue;       // SSE event separator

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                if (line.AsSpan(6).Trim().SequenceEqual("done")) yield break;
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var dataPart = line.AsSpan(5).TrimStart().ToString();
            if (dataPart.Length == 0) continue;

            string? piece;
            try
            {
                piece = JsonSerializer.Deserialize<string>(dataPart, JsonOpts);
            }
            catch (JsonException)
            {
                // Server should always send JSON-encoded strings, but be
                // tolerant of plain text just in case.
                piece = dataPart;
            }

            if (!string.IsNullOrEmpty(piece))
                yield return piece;
        }
    }

    /// <summary>Mirrors <see cref="IButlerService.InvokeToolAsync"/>.</summary>
    public async Task<ToolResult> InvokeToolAsync(ToolInvocation invocation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var payload = new ToolPayload
        {
            ToolName = invocation.ToolName,
            Arguments = new Dictionary<string, object?>(invocation.Arguments),
        };

        using var response = await _http.PostAsJsonAsync("butler/tool", payload, JsonOpts, ct).ConfigureAwait(false);
        // We accept both 200 (success) and 502 (tool failure) — the body is
        // a ToolResult either way. Anything else is a transport error.
        if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.BadGateway)
            response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ToolResult>(JsonOpts, ct).ConfigureAwait(false);
        return result ?? new ToolResult
        {
            ToolName = invocation.ToolName,
            Success = false,
            Error = "Empty response from Butler endpoint.",
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private static HttpClient BuildDefaultHttpClient(int port, string token)
    {
        if (port <= 0) throw new ArgumentOutOfRangeException(nameof(port));
        ArgumentException.ThrowIfNullOrEmpty(token);

        var client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            // Generation can take a while; keep streaming alive but cap
            // overall request time at five minutes by default. Callers can
            // override via the HttpClient overload.
            Timeout = TimeSpan.FromMinutes(5),
        };
        client.DefaultRequestHeaders.Add("X-Butler-Token", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static List<ChatMessagePayload> ConvertMessages(IReadOnlyList<ChatMessage> messages)
    {
        var list = new List<ChatMessagePayload>(messages.Count);
        foreach (var m in messages)
            list.Add(new ChatMessagePayload { Role = m.Role, Content = m.Content });
        return list;
    }

    // ------------------------------------------------------------------
    // wire payloads (mirror HttpLoopbackEndpoint)
    // ------------------------------------------------------------------

    private sealed class AskPayload
    {
        public string? Question { get; set; }
    }

    private sealed class ChatMessagePayload
    {
        public string? Role { get; set; }
        public string? Content { get; set; }
    }

    private sealed class GenerationOptionsPayload
    {
        public int? MaxTokens { get; set; }
        public float? Temperature { get; set; }
        public float? TopP { get; set; }
        public int? TopK { get; set; }
        public int? Seed { get; set; }
        public string[]? StopSequences { get; set; }

        public static GenerationOptionsPayload From(GenerationOptions o) => new()
        {
            MaxTokens = o.MaxTokens,
            Temperature = o.Temperature,
            TopP = o.TopP,
            TopK = o.TopK,
            Seed = o.Seed,
            StopSequences = o.StopSequences,
        };
    }

    private sealed class ChatPayload
    {
        public List<ChatMessagePayload>? Messages { get; set; }
        public GenerationOptionsPayload? Options { get; set; }
    }

    private sealed class ChatResponse
    {
        public string Content { get; set; } = string.Empty;
    }

    private sealed class ToolPayload
    {
        public string? ToolName { get; set; }
        public Dictionary<string, object?>? Arguments { get; set; }
    }
}
