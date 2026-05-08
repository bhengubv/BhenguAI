using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bhengu.AI.Tools;
using Xunit;

namespace Bhengu.AI.Tests;

public sealed class ToolCatalogueTests
{
    // ------------------------------------------------------------------
    // TheGeekNetworkTools catalogue integrity
    // ------------------------------------------------------------------

    [Fact]
    public void GetAllTools_ReturnsNonEmptyList()
    {
        var tools = TheGeekNetworkTools.GetAllTools();
        Assert.NotEmpty(tools);
    }

    [Fact]
    public void GetAllTools_AllNamesAreUnique()
    {
        var tools = TheGeekNetworkTools.GetAllTools();
        var names = tools.Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void GetAllTools_AllNamesMatchTgnPattern()
    {
        // Every tool name must start with "tgn." and contain at least two dots.
        var tools = TheGeekNetworkTools.GetAllTools();
        foreach (var tool in tools)
        {
            Assert.StartsWith("tgn.", tool.Name, StringComparison.Ordinal);
            Assert.True(tool.Name.Count(c => c == '.') >= 2,
                $"Tool '{tool.Name}' must have pattern tgn.<api>.<verb>");
        }
    }

    [Fact]
    public void GetAllTools_RequiredParamsExistInParameters()
    {
        var tools = TheGeekNetworkTools.GetAllTools();
        foreach (var tool in tools)
        {
            foreach (var required in tool.RequiredParameters)
            {
                Assert.True(tool.Parameters.ContainsKey(required),
                    $"Tool '{tool.Name}': required param '{required}' not in Parameters.");
            }
        }
    }

    [Fact]
    public void GetAllTools_NoNullDescriptions()
    {
        var tools = TheGeekNetworkTools.GetAllTools();
        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Name),
                "Tool has null/empty Name.");
            Assert.False(string.IsNullOrWhiteSpace(tool.Description),
                $"Tool '{tool.Name}' has null/empty Description.");
        }
    }

    [Fact]
    public void IndividualApis_CoverExpectedCount()
    {
        // Spot-check a few well-known APIs exist and have the right tool count.
        Assert.Equal(3, TheGeekNetworkTools.Auth().Count);    // request_otp, verify_otp, push_to_app
        Assert.Equal(3, TheGeekNetworkTools.BidBaas().Count);  // list, place_bid, get_details
        Assert.Equal(3, TheGeekNetworkTools.Sdpkt().Count);    // get_balance, send_payment, get_transactions
        Assert.Equal(2, TheGeekNetworkTools.Panik().Count);    // trigger_sos, cancel_sos
    }

    // ------------------------------------------------------------------
    // ToolManifestGenerator — JSON format
    // ------------------------------------------------------------------

    [Fact]
    public void GenerateJsonManifest_ProducesValidJson()
    {
        var tools = TheGeekNetworkTools.Auth();
        var json  = ToolManifestGenerator.GenerateJsonManifest(tools);

        // Must parse without throwing
        var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    [Fact]
    public void GenerateJsonManifest_ContainsAllTools()
    {
        var tools = TheGeekNetworkTools.Auth();
        var json  = ToolManifestGenerator.GenerateJsonManifest(tools);

        var doc   = JsonDocument.Parse(json);
        Assert.Equal(tools.Count, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public void GenerateJsonManifest_HasExpectedShape()
    {
        var tools = new[]
        {
            new ToolDefinition
            {
                Name        = "tgn.test.do_thing",
                Description = "A test tool.",
                Parameters  = new Dictionary<string, ToolParameter>
                {
                    ["id"] = new() { Type = "string", Description = "An id." },
                },
                RequiredParameters = new[] { "id" },
            }
        };

        var json = ToolManifestGenerator.GenerateJsonManifest(tools);
        var doc  = JsonDocument.Parse(json);
        var first = doc.RootElement[0];

        Assert.Equal("function", first.GetProperty("type").GetString());
        var fn = first.GetProperty("function");
        Assert.Equal("tgn.test.do_thing", fn.GetProperty("name").GetString());
    }

    [Fact]
    public void GenerateJsonManifest_EmptyList_ReturnsEmptyArray()
    {
        var json = ToolManifestGenerator.GenerateJsonManifest(Array.Empty<ToolDefinition>());
        var doc  = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public void GenerateJsonManifest_NullList_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ToolManifestGenerator.GenerateJsonManifest(null!));
    }

    // ------------------------------------------------------------------
    // ToolManifestGenerator — Markdown format
    // ------------------------------------------------------------------

    [Fact]
    public void GenerateMarkdownManifest_ContainsHeader()
    {
        var tools = TheGeekNetworkTools.Auth();
        var md    = ToolManifestGenerator.GenerateMarkdownManifest(tools);
        Assert.Contains("# Available Tools", md, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateMarkdownManifest_ContainsToolNames()
    {
        var tools = TheGeekNetworkTools.Auth();
        var md    = ToolManifestGenerator.GenerateMarkdownManifest(tools);

        foreach (var tool in tools)
            Assert.Contains(tool.Name, md, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateMarkdownManifest_NullList_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ToolManifestGenerator.GenerateMarkdownManifest(null!));
    }

    // ------------------------------------------------------------------
    // HttpToolBridge — unregistered tool returns failure gracefully
    // ------------------------------------------------------------------

    [Fact]
    public async Task HttpToolBridge_UnknownTool_ReturnsFailureResult()
    {
        var bridge = new HttpToolBridge("https://api.thegeek.co.za/", new HttpClient());

        var result = await bridge.InvokeAsync(new ToolInvocation
        {
            ToolName  = "tgn.does_not_exist.foobar",
            Arguments = new Dictionary<string, object?>(),
        });

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void HttpToolBridge_AvailableTools_MatchCatalogue()
    {
        var bridge = new HttpToolBridge("https://api.thegeek.co.za/", new HttpClient());
        Assert.Equal(
            TheGeekNetworkTools.GetAllTools().Count,
            bridge.AvailableTools.Count);
    }
}
