using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Circle.AI.Hosting;
using Circle.AI.Hosting.Endpoints;
using Circle.AI.Inference;
using Circle.AI.Tools;
using Xunit;

namespace Circle.AI.Tests;

// ============================================================================
// AIHttpClient — constructor guards (no network needed)
// ============================================================================

public sealed class AIHttpClientConstructorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-9999)]
    public void Constructor_InvalidPort_ThrowsArgumentOutOfRange(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AIHttpClient(port, "valid-token"));
    }

    [Fact]
    public void Constructor_NullToken_ThrowsArgumentNullException()
    {
        // ArgumentException.ThrowIfNullOrEmpty raises ArgumentNullException for null.
        Assert.Throws<ArgumentNullException>(() => new AIHttpClient(8080, null!));
    }

    [Fact]
    public void Constructor_EmptyToken_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new AIHttpClient(8080, ""));
    }

    [Fact]
    public void Constructor_NullHttpClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new AIHttpClient(null!, ownsClient: false));
    }

    [Fact]
    public void Constructor_ValidPort_Succeeds()
    {
        using var client = new AIHttpClient(8080, "some-token");
        Assert.NotNull(client);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:9999/") };
        var client = new AIHttpClient(http, ownsClient: false);
        client.Dispose();
        var ex = Record.Exception(() => client.Dispose());
        Assert.Null(ex);
    }
}

// ============================================================================
// AIHttpClient — argument guards (no server needed, just immediate throws)
// ============================================================================

public sealed class AIHttpClientArgTests
{
    [Fact]
    public async Task AskAsync_NullOrEmptyQuestion_ThrowsArgumentException()
    {
        // The throw happens before any network call so no live server is needed.
        using var client = new AIHttpClient(9999, "tok");
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.AskAsync(null!));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.AskAsync(""));
    }

    [Fact]
    public async Task ChatAsync_NullMessages_ThrowsArgumentNullException()
    {
        using var client = new AIHttpClient(9999, "tok");
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.ChatAsync(null!));
    }

    [Fact]
    public async Task InvokeToolAsync_NullInvocation_ThrowsArgumentNullException()
    {
        using var client = new AIHttpClient(9999, "tok");
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.InvokeToolAsync(null!));
    }
}

// ============================================================================
// AIHttpClient — integration tests against a live HttpLoopbackEndpoint
// ============================================================================

public sealed class AIHttpClientIntegrationTests : IAsyncLifetime
{
    private readonly string _modelPath = Path.GetTempFileName();
    private readonly FakeChatGenerator _generator =
        new("http-client-reply", new[] { "http", "-", "chunk" });

    private AIService? _service;
    private HttpLoopbackEndpoint? _endpoint;
    private AIHttpClient? _client;

    public async Task InitializeAsync()
    {
        var opts = new AIOptions
        {
            ModelPath   = _modelPath,
            WarmOnStart = false,
            ToolBridge  = new FakeToolBridge(
                new ToolResult { ToolName = "fake", Success = true, Result = "42" }),
        };
        _service = new AIService(opts, generatorFactory: _ => _generator);
        await _service.StartAsync();

        _endpoint = new HttpLoopbackEndpoint(new AIOptions { ModelPath = _modelPath });
        await _endpoint.StartAsync(_service);

        _client = new AIHttpClient(_endpoint.BoundPort, _endpoint.Token!);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_endpoint is not null) await _endpoint.DisposeAsync();
        if (_service  is not null) await _service.DisposeAsync();
        try { File.Delete(_modelPath); } catch { /* best-effort */ }
    }

    // -----------------------------------------------------------------------
    // AskAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AskAsync_ReturnsExpectedReply()
    {
        var reply = await _client!.AskAsync("what is 6 × 7?");
        Assert.Equal("http-client-reply", reply);
    }

    // -----------------------------------------------------------------------
    // ChatAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ChatAsync_ReturnsExpectedReply()
    {
        var reply = await _client!.ChatAsync(
            new[] { new ChatMessage("user", "hello") });
        Assert.Equal("http-client-reply", reply);
    }

    [Fact]
    public async Task ChatAsync_EmptyMessages_Returns400()
    {
        // The endpoint validates that at least one message is present.
        // An empty list should result in a 400 → HttpRequestException from EnsureSuccessStatusCode.
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            _client!.ChatAsync(new List<ChatMessage>()));
    }

    // -----------------------------------------------------------------------
    // StreamAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StreamAsync_YieldsAllChunks()
    {
        var chunks = new List<string>();
        await foreach (var piece in _client!.StreamAsync(
            new[] { new ChatMessage("user", "stream me") }))
        {
            chunks.Add(piece);
        }

        Assert.Equal(new[] { "http", "-", "chunk" }, chunks);
    }

    // -----------------------------------------------------------------------
    // InvokeToolAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InvokeToolAsync_WithBridge_ReturnsSuccess()
    {
        var result = await _client!.InvokeToolAsync(new ToolInvocation
        {
            ToolName  = "fake",
            Arguments = new Dictionary<string, object?>(),
        });

        Assert.True(result.Success);
        // Result is deserialized from JSON; the value "42" comes back as JsonElement.
        Assert.NotNull(result.Result);
    }
}
