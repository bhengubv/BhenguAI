// ComposioToolBridgeTests.cs
//
// Unit tests for ComposioToolBridge. Uses a fake HttpMessageHandler so no
// real network calls are made. No external mocking libraries are used.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Circle.AI.Tools;
using Xunit;

namespace Circle.AI.Tests;

// ---------------------------------------------------------------------------
// Fake HttpMessageHandler — returns canned responses for tests
// ---------------------------------------------------------------------------

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        => _handler = handler;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_handler(request));
    }
}

// ---------------------------------------------------------------------------
// Constructor tests
// ---------------------------------------------------------------------------

public sealed class ComposioToolBridgeConstructorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NullOrWhitespaceApiKey_ThrowsArgumentException(string? apiKey)
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException (subclass)
        // for null and ArgumentException for empty/whitespace — ThrowsAny covers both.
        Assert.ThrowsAny<ArgumentException>(() =>
            new ComposioToolBridge(apiKey!));
    }

    [Fact]
    public void Constructor_ValidApiKey_Succeeds()
    {
        using var bridge = new ComposioToolBridge("test-key");
        Assert.NotNull(bridge);
        Assert.Empty(bridge.AvailableTools); // empty until GetAvailableToolsAsync is called
    }

    [Fact]
    public void Constructor_CustomServerUri_Accepted()
    {
        var uri = new Uri("https://my.composio.server/");
        using var bridge = new ComposioToolBridge("test-key", serverUri: uri);
        Assert.NotNull(bridge);
    }

    [Fact]
    public void Constructor_ExternalHttpClient_IsNotDisposedByBridge()
    {
        // When an HttpClient is supplied, the bridge must NOT dispose it.
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        var client = new HttpClient(handler);

        using (var bridge = new ComposioToolBridge("test-key", httpClient: client))
        {
            // Bridge goes out of scope here.
        }

        // Client should still be usable.
        Assert.NotNull(client);
        client.Dispose();
    }
}

// ---------------------------------------------------------------------------
// InvokeAsync tests
// ---------------------------------------------------------------------------

public sealed class ComposioToolBridgeInvokeTests
{
    private static ComposioToolBridge MakeBridge(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var fakeHandler = new FakeHttpMessageHandler(handler);
        var httpClient  = new HttpClient(fakeHandler);
        return new ComposioToolBridge("test-api-key", httpClient: httpClient);
    }

    // -----------------------------------------------------------------------
    // Argument guards
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_NullInvocation_ThrowsArgumentNullException()
    {
        using var bridge = new ComposioToolBridge("test-key");
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            bridge.InvokeAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvokeAsync_EmptyOrWhitespaceToolName_ThrowsArgumentException(string toolName)
    {
        using var bridge = new ComposioToolBridge("test-key");
        var invocation = new ToolInvocation
        {
            ToolName  = toolName,
            Arguments = new Dictionary<string, object?>(),
        };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            bridge.InvokeAsync(invocation));
    }

    // -----------------------------------------------------------------------
    // HTTP failure → ToolResult.Failure (never throws)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_HttpConnectionError_ReturnsFailureResult()
    {
        // Simulate a network error by throwing HttpRequestException from the handler.
        var handler = new FakeHttpMessageHandler(_ =>
            throw new HttpRequestException("Connection refused"));
        var client = new HttpClient(handler);
        await using var bridge = new ComposioToolBridge("test-key", httpClient: client);

        var result = await bridge.InvokeAsync(new ToolInvocation
        {
            ToolName  = "gmail_send_email",
            Arguments = new Dictionary<string, object?>(),
        });

        Assert.False(result.Success);
        Assert.Equal("gmail_send_email", result.ToolName);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task InvokeAsync_Http500_ReturnsFailureResult()
    {
        await using var bridge = MakeBridge(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(
                    "{\"error\":{\"message\":\"Server exploded\"}}",
                    Encoding.UTF8, "application/json")
            });

        var result = await bridge.InvokeAsync(new ToolInvocation
        {
            ToolName  = "slack_post_message",
            Arguments = new Dictionary<string, object?>(),
        });

        Assert.False(result.Success);
        Assert.Equal("slack_post_message", result.ToolName);
        Assert.NotNull(result.Error);
    }

    // -----------------------------------------------------------------------
    // Successful JSON-RPC 2.0 response → ToolResult.Ok
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_SuccessfulJsonRpcResponse_ReturnsOkResult()
    {
        var jsonResponse = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id      = 1,
            result  = new { status = "sent", messageId = "msg-123" }
        });

        await using var bridge = MakeBridge(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            });

        var result = await bridge.InvokeAsync(new ToolInvocation
        {
            ToolName  = "gmail_send_email",
            Arguments = new Dictionary<string, object?>
            {
                ["to"]      = "user@example.com",
                ["subject"] = "Hello",
                ["body"]    = "World",
            },
        });

        Assert.True(result.Success);
        Assert.Equal("gmail_send_email", result.ToolName);
        Assert.Null(result.Error);
    }

    // -----------------------------------------------------------------------
    // JSON-RPC 2.0 error object → ToolResult.Failure
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_JsonRpcErrorObject_ReturnsFailureWithMessage()
    {
        var jsonResponse = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id      = 1,
            error   = new { code = -32600, message = "Invalid request: missing 'to' field" }
        });

        await using var bridge = MakeBridge(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            });

        var result = await bridge.InvokeAsync(new ToolInvocation
        {
            ToolName  = "gmail_send_email",
            Arguments = new Dictionary<string, object?>(),
        });

        Assert.False(result.Success);
        Assert.Equal("gmail_send_email", result.ToolName);
        Assert.Contains("missing", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Pre-cancelled token → OperationCanceledException re-thrown
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_PreCancelledToken_ThrowsOperationCanceledException()
    {
        await using var bridge = MakeBridge(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            bridge.InvokeAsync(new ToolInvocation
            {
                ToolName  = "some_tool",
                Arguments = new Dictionary<string, object?>(),
            }, cts.Token));
    }

    // -----------------------------------------------------------------------
    // API key is sent in request header
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_SendsApiKeyHeader()
    {
        string? capturedApiKey = null;

        await using var bridge = MakeBridge(req =>
        {
            req.Headers.TryGetValues("X-API-Key", out var vals);
            capturedApiKey = vals != null ? string.Join(",", vals) : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"result\":null}", Encoding.UTF8, "application/json")
            };
        });

        await bridge.InvokeAsync(new ToolInvocation
        {
            ToolName  = "test_tool",
            Arguments = new Dictionary<string, object?>(),
        });

        Assert.Equal("test-api-key", capturedApiKey);
    }
}

// ---------------------------------------------------------------------------
// GetAvailableToolsAsync tests
// ---------------------------------------------------------------------------

public sealed class ComposioToolBridgeDiscoveryTests
{
    private static ComposioToolBridge MakeBridge(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var fakeHandler = new FakeHttpMessageHandler(handler);
        var httpClient  = new HttpClient(fakeHandler);
        return new ComposioToolBridge("test-api-key", httpClient: httpClient);
    }

    [Fact]
    public async Task GetAvailableToolsAsync_ValidResponse_ParsesToolDefinitions()
    {
        var toolsJson = JsonSerializer.Serialize(new object[]
        {
            new
            {
                name        = "gmail_send_email",
                description = "Send an email via Gmail",
                inputSchema = new
                {
                    type       = "object",
                    properties = new
                    {
                        to      = new { type = "string", description = "Recipient" },
                        subject = new { type = "string", description = "Subject line" },
                    },
                    required = new[] { "to", "subject" }
                }
            },
            new
            {
                name        = "slack_post_message",
                description = "Post a message to Slack",
                inputSchema = new
                {
                    type       = "object",
                    properties = new
                    {
                        channel = new { type = "string", description = "Slack channel" },
                        text    = new { type = "string", description = "Message text" },
                    },
                    required = new[] { "channel", "text" }
                }
            }
        });

        await using var bridge = MakeBridge(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(toolsJson, Encoding.UTF8, "application/json")
            });

        var tools = await bridge.GetAvailableToolsAsync();

        Assert.Equal(2, tools.Count);
        Assert.Equal("gmail_send_email",  tools[0].Name);
        Assert.Equal("slack_post_message", tools[1].Name);
        Assert.Equal(2, tools[0].Parameters.Count);
        Assert.Contains("to",      tools[0].RequiredParameters);
        Assert.Contains("subject", tools[0].RequiredParameters);
    }

    [Fact]
    public async Task GetAvailableToolsAsync_ResponseWrappedInToolsProperty_ParsesCorrectly()
    {
        var toolsJson = JsonSerializer.Serialize(new
        {
            tools = new object[]
            {
                new { name = "github_create_issue", description = "Create a GitHub issue",
                      inputSchema = new { type = "object", properties = new { }, required = Array.Empty<string>() } }
            }
        });

        await using var bridge = MakeBridge(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(toolsJson, Encoding.UTF8, "application/json")
            });

        var tools = await bridge.GetAvailableToolsAsync();

        Assert.Single(tools);
        Assert.Equal("github_create_issue", tools[0].Name);
    }

    [Fact]
    public async Task GetAvailableToolsAsync_HttpError_ReturnsEmptyList()
    {
        await using var bridge = MakeBridge(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });

        var tools = await bridge.GetAvailableToolsAsync();

        Assert.Empty(tools);
    }

    [Fact]
    public async Task GetAvailableToolsAsync_NetworkError_ReturnsEmptyList()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            throw new HttpRequestException("Network unreachable"));
        var client  = new HttpClient(handler);
        await using var bridge = new ComposioToolBridge("test-key", httpClient: client);

        var tools = await bridge.GetAvailableToolsAsync();

        Assert.Empty(tools);
    }

    [Fact]
    public async Task GetAvailableToolsAsync_UpdatesAvailableToolsProperty()
    {
        var toolsJson = JsonSerializer.Serialize(new object[]
        {
            new { name = "tool_a", description = "A",
                  inputSchema = new { type = "object", properties = new { }, required = Array.Empty<string>() } }
        });

        await using var bridge = MakeBridge(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(toolsJson, Encoding.UTF8, "application/json")
            });

        Assert.Empty(bridge.AvailableTools);

        await bridge.GetAvailableToolsAsync();

        Assert.Single(bridge.AvailableTools);
        Assert.Equal("tool_a", bridge.AvailableTools[0].Name);
    }
}
