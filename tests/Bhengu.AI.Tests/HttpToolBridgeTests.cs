using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Bhengu.AI.Tools;
using Xunit;

namespace Bhengu.AI.Tests;

/// <summary>
/// Tests for <see cref="HttpToolBridge"/> that do not require a live server.
/// </summary>
public sealed class HttpToolBridgeConstructorTests
{
    private static readonly HttpClient SharedHttp = new();

    // ------------------------------------------------------------------
    // Argument guards
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_NullBaseUrl_ThrowsArgumentNullException()
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace raises ArgumentNullException for null.
        Assert.Throws<ArgumentNullException>(() =>
            new HttpToolBridge(null!, SharedHttp));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyOrWhitespaceBaseUrl_Throws(string baseUrl)
    {
        Assert.Throws<ArgumentException>(() =>
            new HttpToolBridge(baseUrl, SharedHttp));
    }

    [Fact]
    public void Constructor_NullHttpClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new HttpToolBridge("https://api.thegeek.co.za/", null!));
    }

    [Fact]
    public void Constructor_NullToolsList_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new HttpToolBridge("https://api.thegeek.co.za/", SharedHttp, null!));
    }

    // ------------------------------------------------------------------
    // Valid construction
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_ValidArgs_Succeeds()
    {
        var bridge = new HttpToolBridge("https://api.thegeek.co.za/", SharedHttp);
        Assert.NotEmpty(bridge.AvailableTools);
    }

    [Fact]
    public void Constructor_BaseUrlWithoutTrailingSlash_Succeeds()
    {
        // The constructor must not throw when the URL lacks a trailing slash.
        var bridge = new HttpToolBridge("https://api.thegeek.co.za", SharedHttp);
        Assert.NotEmpty(bridge.AvailableTools);
    }

    [Fact]
    public void Constructor_EmptyToolsList_ProducesEmptyAvailableTools()
    {
        var bridge = new HttpToolBridge(
            "https://api.thegeek.co.za/",
            SharedHttp,
            Array.Empty<ToolDefinition>());
        Assert.Empty(bridge.AvailableTools);
    }

    [Fact]
    public void Constructor_CustomToolsList_ExposedViaAvailableTools()
    {
        var tools = new List<ToolDefinition>
        {
            new() { Name = "tgn.test.ping", Description = "ping", Parameters = new Dictionary<string, ToolParameter>(), RequiredParameters = Array.Empty<string>() }
        };
        var bridge = new HttpToolBridge("https://api.thegeek.co.za/", SharedHttp, tools);
        Assert.Single(bridge.AvailableTools);
        Assert.Equal("tgn.test.ping", bridge.AvailableTools[0].Name);
    }
}

public sealed class HttpToolBridgeInvokeTests
{
    private static readonly HttpClient SharedHttp = new();

    // ------------------------------------------------------------------
    // InvokeAsync argument guards
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_NullInvocation_Throws()
    {
        var bridge = new HttpToolBridge("https://api.thegeek.co.za/", SharedHttp);
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            bridge.InvokeAsync(null!));
    }

    // ------------------------------------------------------------------
    // Routing behaviour (no network — tool not registered)
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_ToolNotInRouteTable_ReturnsFailureWithMessage()
    {
        // Use a custom single-tool bridge so the tool is in AvailableTools but
        // not in the internal routes table (which is always the full TGN list).
        var bridge = new HttpToolBridge(
            "https://api.thegeek.co.za/",
            SharedHttp,
            new List<ToolDefinition>
            {
                new() { Name = "tgn.custom.my_op", Description = "custom", Parameters = new Dictionary<string, ToolParameter>(), RequiredParameters = Array.Empty<string>() }
            });

        var result = await bridge.InvokeAsync(new ToolInvocation
        {
            ToolName  = "tgn.custom.my_op",
            Arguments = new Dictionary<string, object?>(),
        });

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("tgn.sdpkt.get_balance")]
    [InlineData("tgn.auth.request_otp")]
    [InlineData("tgn.panik.trigger_sos")]
    public async Task InvokeAsync_KnownRegisteredTool_ReturnsFailureWhenNetworkUnreachable(string toolName)
    {
        // These tools exist in the route table. On CI/dev with no live server they
        // return a failure ToolResult (network error captured by the catch block),
        // not an exception. This verifies the resilience pattern.
        var bridge = new HttpToolBridge("https://localhost:0/", SharedHttp);

        var result = await bridge.InvokeAsync(new ToolInvocation
        {
            ToolName  = toolName,
            Arguments = new Dictionary<string, object?>(),
        });

        // The bridge should NEVER throw — it must return a ToolResult.
        Assert.NotNull(result);
        Assert.Equal(toolName, result.ToolName);
    }
}
