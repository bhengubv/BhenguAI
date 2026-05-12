// HttpLoopbackEndpoint.cs
//
// Tiny HTTP server that exposes a AIService to other processes on the
// same host (e.g. a CircleOS keyboard talking to a CircleOS background
// service). Implementation is built on System.Net.HttpListener so we have
// zero ASP.NET Core dependency and stay portable across Linux / macOS /
// Windows / Android.
//
// Routes:
//   POST /butler/ask    -> { "question": string }                              -> text/plain
//   POST /butler/chat   -> { "messages": [...], "options": {...} }             -> { "content": string }
//   POST /butler/stream -> { "messages": [...], "options": {...} }             -> text/event-stream
//   POST /butler/tool   -> { "toolName": string, "arguments": {...} }          -> ToolResult JSON
//
// Authentication is a shared-secret header `X-Butler-Token`. Configure via
// AIOptions.LoopbackToken; when not set, the endpoint generates a
// random token at startup and exposes it via Token.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Circle.AI.Inference;
using Circle.AI.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Circle.AI.Hosting.Endpoints;

/// <summary>
/// Loopback HTTP transport for <see cref="IAIService"/>. Binds only to
/// <c>127.0.0.1</c> so we never expose Butler on the network.
/// </summary>
public sealed class HttpLoopbackEndpoint : IAIEndpoint
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    private readonly AIOptions _options;
    private readonly ILogger<HttpLoopbackEndpoint> _logger;

    private HttpListener? _listener;
    private CancellationTokenSource? _serverCts;
    private Task? _acceptLoop;
    private IAIService? _service;
    private string? _token;
    private int _boundPort;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Constructs the endpoint without starting it. Call <see cref="StartAsync"/>
    /// to bind and begin serving.
    /// </summary>
    public HttpLoopbackEndpoint(AIOptions options, ILogger<HttpLoopbackEndpoint>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<HttpLoopbackEndpoint>.Instance;
    }

    /// <summary>
    /// Port the listener is currently bound to. <c>0</c> when not started.
    /// </summary>
    public int BoundPort => _boundPort;

    /// <summary>
    /// Effective shared-secret token. <c>null</c> when not started.
    /// </summary>
    public string? Token => _token;

    /// <inheritdoc />
    public async Task StartAsync(IAIService service, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(service);
        if (_started) return;

        _service = service;
        _token = string.IsNullOrEmpty(_options.LoopbackToken)
            ? AIOptions.GenerateRandomToken()
            : _options.LoopbackToken;

        var configuredPort = _options.LoopbackPort;
        var port = configuredPort > 0 ? configuredPort : PickFreeLoopbackPort();
        _boundPort = port;

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            listener.Close();
            throw new InvalidOperationException(
                $"Failed to start loopback HTTP listener on port {port}. " +
                "On Windows this may require URL ACL configuration; consider using port 0 to let the OS assign a port.",
                ex);
        }

        _listener = listener;
        _serverCts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_serverCts.Token));
        _started = true;

        _logger.LogInformation("Butler HTTP loopback endpoint listening on http://127.0.0.1:{Port}", port);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!_started) return;
        _started = false;

        try { _serverCts?.Cancel(); } catch (ObjectDisposedException) { /* CTS already disposed — nothing to do */ }

        try
        {
            _listener?.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Butler loopback: listener already closed during StopAsync.");
        }

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Caller-supplied cancellation; not fatal.
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Butler loopback: accept loop faulted on shutdown.");
            }
        }

        _listener?.Close();
        _listener = null;
        _serverCts?.Dispose();
        _serverCts = null;
        _acceptLoop = null;
        _service = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // DisposeAsync must not throw — swallow but log at debug level.
            _logger.LogDebug(ex, "Butler loopback: StopAsync threw during DisposeAsync.");
        }
    }

    // ------------------------------------------------------------------
    // Accept loop
    // ------------------------------------------------------------------

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        var listener = _listener;
        if (listener is null) return;

        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Butler accept loop hit an unexpected error.");
                continue;
            }

            // Fire-and-forget: each request runs on its own Task so the accept loop is not blocked.
            // HandleRequestAsync is fully exception-guarded — unhandled throws cannot escape it.
            _ = Task.Run(() => HandleRequestAsync(context, ct), ct);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            if (!Authorise(context))
            {
                await WritePlainAsync(context, 401, "unauthorised").ConfigureAwait(false);
                return;
            }

            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                await WritePlainAsync(context, 405, "method not allowed").ConfigureAwait(false);
                return;
            }

            var path = context.Request.Url?.AbsolutePath ?? string.Empty;
            switch (path)
            {
                case "/butler/ask":
                    await HandleAskAsync(context, ct).ConfigureAwait(false);
                    break;
                case "/butler/chat":
                    await HandleChatAsync(context, ct).ConfigureAwait(false);
                    break;
                case "/butler/stream":
                    await HandleStreamAsync(context, ct).ConfigureAwait(false);
                    break;
                case "/butler/tool":
                    await HandleToolAsync(context, ct).ConfigureAwait(false);
                    break;
                default:
                    await WritePlainAsync(context, 404, "not found").ConfigureAwait(false);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Either the request was cancelled or the endpoint is shutting
            // down — either way we just close the response and move on.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Butler request handler failed.");
            try
            {
                await WritePlainAsync(context, 500, "internal error").ConfigureAwait(false);
            }
            catch (Exception writeEx)
            {
                _logger.LogDebug(writeEx, "Butler loopback: could not send 500 response — client likely disconnected.");
            }
        }
    }

    // ------------------------------------------------------------------
    // Routes
    // ------------------------------------------------------------------

    private async Task HandleAskAsync(HttpListenerContext context, CancellationToken ct)
    {
        var service = RequireService();
        var body = await ReadBodyAsync(context.Request, ct).ConfigureAwait(false);
        var payload = Deserialise<AskPayload>(body);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Question))
        {
            await WritePlainAsync(context, 400, "missing 'question'").ConfigureAwait(false);
            return;
        }

        var answer = await service.AskAsync(payload.Question, ct).ConfigureAwait(false);
        await WritePlainAsync(context, 200, answer).ConfigureAwait(false);
    }

    private async Task HandleChatAsync(HttpListenerContext context, CancellationToken ct)
    {
        var service = RequireService();
        var body = await ReadBodyAsync(context.Request, ct).ConfigureAwait(false);
        var payload = Deserialise<ChatPayload>(body);
        if (payload?.Messages is null || payload.Messages.Count == 0)
        {
            await WritePlainAsync(context, 400, "missing 'messages'").ConfigureAwait(false);
            return;
        }

        var messages = payload.Messages.ConvertAll(m => new ChatMessage(m.Role ?? "user", m.Content ?? string.Empty));
        var options = payload.Options?.ToGenerationOptions();
        var content = await service.ChatAsync(messages, options, ct).ConfigureAwait(false);

        await WriteJsonAsync(context, 200, new ChatResponse { Content = content }).ConfigureAwait(false);
    }

    private async Task HandleStreamAsync(HttpListenerContext context, CancellationToken ct)
    {
        var service = RequireService();
        var body = await ReadBodyAsync(context.Request, ct).ConfigureAwait(false);
        var payload = Deserialise<ChatPayload>(body);
        if (payload?.Messages is null || payload.Messages.Count == 0)
        {
            await WritePlainAsync(context, 400, "missing 'messages'").ConfigureAwait(false);
            return;
        }

        var messages = payload.Messages.ConvertAll(m => new ChatMessage(m.Role ?? "user", m.Content ?? string.Empty));
        var options = payload.Options?.ToGenerationOptions();

        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        context.Response.SendChunked = true;

        await using var writer = new StreamWriter(context.Response.OutputStream, new UTF8Encoding(false))
        {
            AutoFlush = false,
            NewLine = "\n",
        };

        try
        {
            await foreach (var piece in service.StreamAsync(messages, options, ct).ConfigureAwait(false))
            {
                if (ct.IsCancellationRequested) break;
                // Emit data in the standard SSE framing. Encode the payload
                // as JSON so newlines / quotes inside the chunk don't break
                // the framing.
                await writer.WriteAsync("data: ").ConfigureAwait(false);
                await writer.WriteLineAsync(JsonSerializer.Serialize(piece, JsonOpts)).ConfigureAwait(false);
                await writer.WriteLineAsync().ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false);
            }

            // Closing event so clients know we're done cleanly.
            await writer.WriteAsync("event: done").ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
            await writer.WriteAsync("data: {}").ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
            await writer.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            try { context.Response.OutputStream.Close(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Butler loopback: stream output already closed."); }
        }
    }

    private async Task HandleToolAsync(HttpListenerContext context, CancellationToken ct)
    {
        var service = RequireService();
        var body = await ReadBodyAsync(context.Request, ct).ConfigureAwait(false);
        var payload = Deserialise<ToolPayload>(body);
        if (payload is null || string.IsNullOrWhiteSpace(payload.ToolName))
        {
            await WritePlainAsync(context, 400, "missing 'toolName'").ConfigureAwait(false);
            return;
        }

        var args = payload.Arguments ?? new Dictionary<string, object?>();
        var invocation = new ToolInvocation
        {
            ToolName = payload.ToolName,
            Arguments = args,
        };
        var result = await service.InvokeToolAsync(invocation, ct).ConfigureAwait(false);
        await WriteJsonAsync(context, result.Success ? 200 : 502, result).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private bool Authorise(HttpListenerContext context)
    {
        var token = _token;
        if (string.IsNullOrEmpty(token)) return false;

        var supplied = context.Request.Headers["X-Butler-Token"];
        return !string.IsNullOrEmpty(supplied)
            && CryptographicEquals(supplied, token);
    }

    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private IAIService RequireService()
    {
        return _service
            ?? throw new InvalidOperationException("HttpLoopbackEndpoint has no service bound.");
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request, CancellationToken ct)
    {
        using var reader = new StreamReader(
            request.InputStream,
            request.ContentEncoding ?? Encoding.UTF8,
            leaveOpen: false);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    private static T? Deserialise<T>(string body) where T : class
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task WritePlainAsync(HttpListenerContext context, int status, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        try
        {
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        }
        finally
        {
            try { context.Response.OutputStream.Close(); } catch { /* already closed */ }
        }
    }

    private static async Task WriteJsonAsync<T>(HttpListenerContext context, int status, T payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        try
        {
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        }
        finally
        {
            try { context.Response.OutputStream.Close(); } catch { /* already closed */ }
        }
    }

    private static int PickFreeLoopbackPort()
    {
        // Bind a TCP listener to port 0, read the assigned port, release it.
        // There's a tiny race window between releasing and HttpListener
        // grabbing it, but on a single host this is acceptable for our use.
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try
        {
            return ((IPEndPoint)l.LocalEndpoint).Port;
        }
        finally
        {
            l.Stop();
        }
    }

    // ------------------------------------------------------------------
    // wire payloads
    // ------------------------------------------------------------------

    private sealed class AskPayload
    {
        public string? Question { get; set; }
    }

    internal sealed class ChatMessagePayload
    {
        public string? Role { get; set; }
        public string? Content { get; set; }
    }

    internal sealed class GenerationOptionsPayload
    {
        public int? MaxTokens { get; set; }
        public float? Temperature { get; set; }
        public float? TopP { get; set; }
        public int? TopK { get; set; }
        public int? Seed { get; set; }
        public string[]? StopSequences { get; set; }

        public GenerationOptions ToGenerationOptions()
        {
            var defaults = new GenerationOptions();
            return new GenerationOptions
            {
                MaxTokens = MaxTokens ?? defaults.MaxTokens,
                Temperature = Temperature ?? defaults.Temperature,
                TopP = TopP ?? defaults.TopP,
                TopK = TopK ?? defaults.TopK,
                Seed = Seed,
                StopSequences = StopSequences,
            };
        }
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
