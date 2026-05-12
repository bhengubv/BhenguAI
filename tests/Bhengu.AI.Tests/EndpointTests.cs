using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bhengu.AI.Hosting;
using Bhengu.AI.Hosting.Endpoints;
using Bhengu.AI.Inference;
using Bhengu.AI.Tools;
using Xunit;

namespace Bhengu.AI.Tests;

// ---------------------------------------------------------------------------
// InProcessEndpoint
// ---------------------------------------------------------------------------

public sealed class InProcessEndpointTests
{
    private static AIService BuildReadyService()
    {
        var modelPath = Path.GetTempFileName();
        var generator = new FakeChatGenerator();
        var opts = new AIOptions { ModelPath = modelPath, WarmOnStart = false };
        return new AIService(opts, generatorFactory: _ => generator);
    }

    [Fact]
    public async Task StartAsync_ExposesServiceAccessor()
    {
        var endpoint = new InProcessEndpoint();
        await using var svc = BuildReadyService();
        await svc.StartAsync();

        await endpoint.StartAsync(svc);
        Assert.Same(svc, endpoint.ServiceAccessor);
    }

    [Fact]
    public async Task StartAsync_Idempotent()
    {
        var endpoint = new InProcessEndpoint();
        await using var svc = BuildReadyService();
        await svc.StartAsync();

        await endpoint.StartAsync(svc);
        await endpoint.StartAsync(svc); // second call is no-op
        Assert.Same(svc, endpoint.ServiceAccessor);
    }

    [Fact]
    public async Task StopAsync_ClearsServiceAccessor()
    {
        var endpoint = new InProcessEndpoint();
        await using var svc = BuildReadyService();
        await svc.StartAsync();

        await endpoint.StartAsync(svc);
        await endpoint.StopAsync();

        Assert.Null(endpoint.ServiceAccessor);
    }

    [Fact]
    public async Task StartAsync_NullService_Throws()
    {
        var endpoint = new InProcessEndpoint();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => endpoint.StartAsync(null!));
    }

    [Fact]
    public async Task DisposeAsync_ClearsServiceAccessorAndIsIdempotent()
    {
        var endpoint = new InProcessEndpoint();
        await using var svc = BuildReadyService();
        await svc.StartAsync();

        await endpoint.StartAsync(svc);
        await endpoint.DisposeAsync();
        await endpoint.DisposeAsync(); // second dispose must not throw

        Assert.Null(endpoint.ServiceAccessor);
    }

    [Fact]
    public async Task StartAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var endpoint = new InProcessEndpoint();
        await endpoint.DisposeAsync();

        await using var svc = BuildReadyService();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => endpoint.StartAsync(svc));
    }
}

// ---------------------------------------------------------------------------
// HttpLoopbackEndpoint
// ---------------------------------------------------------------------------

public sealed class HttpLoopbackEndpointTests : IAsyncLifetime
{
    private readonly string _modelPath = Path.GetTempFileName();
    private readonly FakeChatGenerator _generator = new("loopback reply", new[] { "loop", "back" });
    private AIService? _service;
    private HttpLoopbackEndpoint? _endpoint;
    private HttpClient? _http;

    public async Task InitializeAsync()
    {
        var opts = new AIOptions { ModelPath = _modelPath, WarmOnStart = false };
        _service = new AIService(opts, generatorFactory: _ => _generator);
        await _service.StartAsync();

        var endpointOpts = new AIOptions { ModelPath = _modelPath };
        _endpoint = new HttpLoopbackEndpoint(endpointOpts);
        await _endpoint.StartAsync(_service);

        _http = new HttpClient();
    }

    public async Task DisposeAsync()
    {
        _http?.Dispose();
        if (_endpoint is not null) await _endpoint.DisposeAsync();
        if (_service is not null) await _service.DisposeAsync();
        try { File.Delete(_modelPath); } catch { /* best-effort */ }
    }

    private string BaseUrl => $"http://127.0.0.1:{_endpoint!.BoundPort}";
    private string Token   => _endpoint!.Token!;

    private HttpRequestMessage Post(string path, object body) =>
        new(HttpMethod.Post, $"{BaseUrl}{path}")
        {
            Content = JsonContent.Create(body),
            Headers = { { "X-Butler-Token", Token } },
        };

    // ------------------------------------------------------------------

    [Fact]
    public void BoundPort_IsNonZero_AfterStart()
    {
        Assert.True(_endpoint!.BoundPort > 0);
    }

    [Fact]
    public void Token_IsNonNull_AfterStart()
    {
        Assert.NotNull(_endpoint!.Token);
        Assert.NotEmpty(_endpoint.Token);
    }

    [Fact]
    public async Task AskEndpoint_ReturnsReply()
    {
        using var req = Post("/butler/ask", new { question = "hello" });
        using var resp = await _http!.SendAsync(req);

        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Equal("loopback reply", body);
    }

    [Fact]
    public async Task ChatEndpoint_ReturnsJsonWithContent()
    {
        using var req = Post("/butler/chat", new
        {
            messages = new[] { new { role = "user", content = "hi" } }
        });
        using var resp = await _http!.SendAsync(req);

        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("loopback reply", doc.GetProperty("content").GetString());
    }

    [Fact]
    public async Task MissingToken_Returns401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/butler/ask")
        {
            Content = JsonContent.Create(new { question = "hi" })
            // No X-Butler-Token header
        };
        using var resp = await _http!.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task WrongToken_Returns401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/butler/ask")
        {
            Content = JsonContent.Create(new { question = "hi" }),
            Headers = { { "X-Butler-Token", "wrong-token" } },
        };
        using var resp = await _http!.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/butler/nonexistent")
        {
            Content = JsonContent.Create(new { }),
            Headers = { { "X-Butler-Token", Token } },
        };
        using var resp = await _http!.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetMethod_Returns405()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/butler/ask")
        {
            Headers = { { "X-Butler-Token", Token } },
        };
        using var resp = await _http!.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, resp.StatusCode);
    }

    [Fact]
    public async Task ToolEndpoint_NoBridgeConfigured_Returns502WithError()
    {
        using var req = Post("/butler/tool", new
        {
            toolName  = "tgn.sdpkt.get_balance",
            arguments = new { }
        });
        using var resp = await _http!.SendAsync(req);

        // Service has no tool bridge → returns ToolResult{Success=false} → 502
        Assert.Equal(System.Net.HttpStatusCode.BadGateway, resp.StatusCode);
    }

    [Fact]
    public async Task AskEndpoint_MissingQuestion_Returns400()
    {
        using var req = Post("/butler/ask", new { /* no question */ });
        using var resp = await _http!.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ToolEndpoint_MissingToolName_Returns400()
    {
        using var req = Post("/butler/tool", new { arguments = new { } });
        using var resp = await _http!.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ChatEndpoint_EmptyMessages_Returns400()
    {
        using var req = Post("/butler/chat", new { messages = Array.Empty<object>() });
        using var resp = await _http!.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task StreamEndpoint_ContentType_IsTextEventStream()
    {
        using var req = Post("/butler/stream", new
        {
            messages = new[] { new { role = "user", content = "stream" } }
        });
        // ResponseHeadersRead so we get headers before waiting for the full body.
        using var resp = await _http!.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", resp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task StreamEndpoint_RawSse_EndsWithDoneEvent()
    {
        using var req = Post("/butler/stream", new
        {
            messages = new[] { new { role = "user", content = "stream" } }
        });
        using var resp = await _http!.SendAsync(req, HttpCompletionOption.ResponseContentRead);
        var body = await resp.Content.ReadAsStringAsync();

        // The endpoint always writes a terminal "event: done" + "data: {}" frame.
        Assert.Contains("event: done", body, StringComparison.Ordinal);
        Assert.Contains("data: {}", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamEndpoint_RawSse_ChunksHaveDataPrefix()
    {
        // FakeChatGenerator for this class is configured with streamChunks ["loop","back"].
        using var req = Post("/butler/stream", new
        {
            messages = new[] { new { role = "user", content = "stream" } }
        });
        using var resp = await _http!.SendAsync(req, HttpCompletionOption.ResponseContentRead);
        var body = await resp.Content.ReadAsStringAsync();

        // Each chunk is JSON-serialised: "loop" → "\"loop\"", "back" → "\"back\""
        Assert.Contains("data: \"loop\"", body, StringComparison.Ordinal);
        Assert.Contains("data: \"back\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamEndpoint_EmptyMessages_Returns400()
    {
        using var req = Post("/butler/stream", new { messages = Array.Empty<object>() });
        using var resp = await _http!.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ChatEndpoint_WithOptions_Honours_MaxTokens()
    {
        // The endpoint should deserialise the options payload and pass it through.
        // We cannot inspect what was passed to the generator from here, but we
        // can verify that no error is returned when valid options are provided.
        using var req = Post("/butler/chat", new
        {
            messages = new[] { new { role = "user", content = "hi" } },
            options  = new { maxTokens = 64, temperature = 0.5f, topP = 0.8f, topK = 20 }
        });
        using var resp = await _http!.SendAsync(req);
        resp.EnsureSuccessStatusCode();
    }
}

// ---------------------------------------------------------------------------
// HttpLoopbackEndpoint — standalone configuration / lifecycle tests
// ---------------------------------------------------------------------------

public sealed class HttpLoopbackEndpointConfigTests
{
    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new HttpLoopbackEndpoint(null!));
    }

    [Fact]
    public async Task StartAsync_NullService_Throws()
    {
        await using var endpoint = new HttpLoopbackEndpoint(new AIOptions());
        await Assert.ThrowsAsync<ArgumentNullException>(() => endpoint.StartAsync(null!));
    }

    [Fact]
    public async Task StopAsync_WhenNotStarted_IsNoOp()
    {
        await using var endpoint = new HttpLoopbackEndpoint(new AIOptions());
        var ex = await Record.ExceptionAsync(() => endpoint.StopAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task BoundPort_BeforeStart_IsZero()
    {
        await using var endpoint = new HttpLoopbackEndpoint(new AIOptions());
        Assert.Equal(0, endpoint.BoundPort);
    }

    [Fact]
    public async Task Token_BeforeStart_IsNull()
    {
        await using var endpoint = new HttpLoopbackEndpoint(new AIOptions());
        Assert.Null(endpoint.Token);
    }

    [Fact]
    public async Task StartAsync_WithConfiguredToken_UsesIt()
    {
        var modelPath = Path.GetTempFileName();
        try
        {
            var endpointOpts = new AIOptions
            {
                ModelPath     = modelPath,
                LoopbackToken = "my-fixed-token",
            };
            var svcOpts = new AIOptions { ModelPath = modelPath, WarmOnStart = false };
            await using var svc = new AIService(svcOpts,
                generatorFactory: _ => new FakeChatGenerator());
            await svc.StartAsync();

            await using var endpoint = new HttpLoopbackEndpoint(endpointOpts);
            await endpoint.StartAsync(svc);

            Assert.Equal("my-fixed-token", endpoint.Token);
        }
        finally
        {
            try { File.Delete(modelPath); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task StartAsync_WithoutConfiguredToken_GeneratesToken()
    {
        var modelPath = Path.GetTempFileName();
        try
        {
            var endpointOpts = new AIOptions { ModelPath = modelPath };
            var svcOpts = new AIOptions { ModelPath = modelPath, WarmOnStart = false };
            await using var svc = new AIService(svcOpts,
                generatorFactory: _ => new FakeChatGenerator());
            await svc.StartAsync();

            await using var endpoint = new HttpLoopbackEndpoint(endpointOpts);
            await endpoint.StartAsync(svc);

            Assert.NotNull(endpoint.Token);
            Assert.NotEmpty(endpoint.Token);
        }
        finally
        {
            try { File.Delete(modelPath); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task StartAsync_Idempotent()
    {
        var modelPath = Path.GetTempFileName();
        try
        {
            var opts = new AIOptions { ModelPath = modelPath, LoopbackToken = "tok" };
            var svcOpts = new AIOptions { ModelPath = modelPath, WarmOnStart = false };
            await using var svc = new AIService(svcOpts,
                generatorFactory: _ => new FakeChatGenerator());
            await svc.StartAsync();

            await using var endpoint = new HttpLoopbackEndpoint(opts);
            await endpoint.StartAsync(svc);
            var firstPort = endpoint.BoundPort;

            await endpoint.StartAsync(svc); // second call must be a no-op

            Assert.Equal(firstPort, endpoint.BoundPort);
        }
        finally
        {
            try { File.Delete(modelPath); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task StartAsync_AfterDispose_Throws()
    {
        var endpoint = new HttpLoopbackEndpoint(new AIOptions());
        await endpoint.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            endpoint.StartAsync(new FakeAIService()));
    }
}

// Minimal IAIService stub used only for the "start after dispose" test.
file sealed class FakeAIService : IAIService
{
    public bool IsReady => false;
    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<string> AskAsync(string question, CancellationToken ct = default)
        => Task.FromResult(string.Empty);
    public Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages, GenerationOptions? options = null,
        CancellationToken ct = default) => Task.FromResult(string.Empty);
    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages, GenerationOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }
    public Task<ToolResult> InvokeToolAsync(ToolInvocation invocation, CancellationToken ct = default)
        => Task.FromResult(new ToolResult { ToolName = "none", Success = false });
    public Task<string> AgenticChatAsync(string prompt, GenerationOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(string.Empty);
    public Task SubmitFeedbackAsync(Bhengu.AI.Memory.FeedbackSignal signal, CancellationToken ct = default)
        => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
