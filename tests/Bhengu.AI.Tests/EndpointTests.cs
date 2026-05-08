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
    private static ButlerService BuildReadyService()
    {
        var modelPath = Path.GetTempFileName();
        var generator = new FakeChatGenerator();
        var opts = new ButlerOptions { ModelPath = modelPath, WarmOnStart = false };
        return new ButlerService(opts, generatorFactory: _ => generator);
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
    private ButlerService? _service;
    private HttpLoopbackEndpoint? _endpoint;
    private HttpClient? _http;

    public async Task InitializeAsync()
    {
        var opts = new ButlerOptions { ModelPath = _modelPath, WarmOnStart = false };
        _service = new ButlerService(opts, generatorFactory: _ => _generator);
        await _service.StartAsync();

        var endpointOpts = new ButlerOptions { ModelPath = _modelPath };
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
}
