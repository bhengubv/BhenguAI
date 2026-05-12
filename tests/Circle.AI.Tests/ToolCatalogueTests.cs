using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Circle.AI.Tools;
using Xunit;

namespace Circle.AI.Tests;

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

    [Fact]
    public void GenerateMarkdownManifest_NoParamTool_ProducesNoParametersPlaceholder()
    {
        var tools = new[]
        {
            new ToolDefinition
            {
                Name               = "tgn.auth.request_otp",
                Description        = "Request an OTP.",
                Parameters         = new Dictionary<string, ToolParameter>(),
                RequiredParameters = Array.Empty<string>(),
            }
        };

        var md = ToolManifestGenerator.GenerateMarkdownManifest(tools);
        Assert.Contains("_No parameters._", md, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateMarkdownManifest_PipeInDescription_IsEscaped()
    {
        // Pipe characters in parameter descriptions must be escaped so they
        // don't break the Markdown table.
        var tools = new[]
        {
            new ToolDefinition
            {
                Name               = "tgn.test.pipe_test",
                Description        = "A tool.",
                Parameters         = new Dictionary<string, ToolParameter>
                {
                    ["side"] = new() { Type = "string", Description = "left|right|center" },
                },
                RequiredParameters = new[] { "side" },
            }
        };

        var md = ToolManifestGenerator.GenerateMarkdownManifest(tools);
        Assert.Contains("left\\|right\\|center", md, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateJsonManifest_EnumParameter_IncludedInOutput()
    {
        // ToolParameters with Enum values must propagate into the JSON manifest
        // so the LLM knows which values are allowed.
        var tools = new[]
        {
            new ToolDefinition
            {
                Name               = "tgn.test.enum_param",
                Description        = "Enum test.",
                Parameters         = new Dictionary<string, ToolParameter>
                {
                    ["direction"] = new()
                    {
                        Type        = "string",
                        Description = "Direction.",
                        Enum        = new[] { "north", "south" },
                    },
                },
                RequiredParameters = new[] { "direction" },
            }
        };

        var json = ToolManifestGenerator.GenerateJsonManifest(tools);
        Assert.Contains("north",  json, StringComparison.Ordinal);
        Assert.Contains("south",  json, StringComparison.Ordinal);
        Assert.Contains("\"enum\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateJsonManifest_NoEnumParameter_OmitsEnumKey()
    {
        // Parameters WITHOUT enum values must NOT emit an "enum" key
        // (WhenWritingNull is configured; the key should be absent entirely).
        var tools = new[]
        {
            new ToolDefinition
            {
                Name               = "tgn.test.no_enum",
                Description        = "No enum.",
                Parameters         = new Dictionary<string, ToolParameter>
                {
                    ["text"] = new() { Type = "string", Description = "free text" },
                },
                RequiredParameters = Array.Empty<string>(),
            }
        };

        var json = ToolManifestGenerator.GenerateJsonManifest(tools);
        Assert.DoesNotContain("\"enum\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateMarkdownManifest_ToolsGroupedByApiSlug()
    {
        // Tools with the same tgn.<api> prefix should be grouped under
        // the same "## tgn.<api>" heading.
        var tools = new[]
        {
            new ToolDefinition
            {
                Name               = "tgn.sdpkt.get_balance",
                Description        = "Get balance.",
                Parameters         = new Dictionary<string, ToolParameter>(),
                RequiredParameters = Array.Empty<string>(),
            },
            new ToolDefinition
            {
                Name               = "tgn.sdpkt.send_payment",
                Description        = "Send payment.",
                Parameters         = new Dictionary<string, ToolParameter>(),
                RequiredParameters = Array.Empty<string>(),
            },
        };

        var md = ToolManifestGenerator.GenerateMarkdownManifest(tools);
        // Both tools share the "tgn.sdpkt" group heading.
        Assert.Contains("## tgn.sdpkt", md, StringComparison.Ordinal);
        // The heading appears exactly once (not once per tool).
        var headingCount = md.Split(new[] { "## tgn.sdpkt" }, StringSplitOptions.None).Length - 1;
        Assert.Equal(1, headingCount);
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

// ============================================================================
// TheGeekNetworkTools — per-API smoke tests
// Ensures every individual API method returns a non-empty, well-formed list.
// ============================================================================

public sealed class TheGeekNetworkToolsApiCoverageTests
{
    [Theory]
    [InlineData("Account")]
    [InlineData("Audit")]
    [InlineData("Auth")]
    [InlineData("BidBaas")]
    [InlineData("BillPayment")]
    [InlineData("Blockchain")]
    [InlineData("Butler")]
    [InlineData("CircleAether")]
    [InlineData("Ecommerce")]
    [InlineData("Electricity")]
    [InlineData("Geo")]
    [InlineData("Glocell")]
    [InlineData("Incentives")]
    [InlineData("KiffStore")]
    [InlineData("Ledger")]
    [InlineData("Localization")]
    [InlineData("Maps")]
    [InlineData("MapsData")]
    [InlineData("Media")]
    [InlineData("Messaging")]
    [InlineData("Notification")]
    [InlineData("OpSupport")]
    [InlineData("Panik")]
    [InlineData("Payfast")]
    [InlineData("Sdpkt")]
    [InlineData("ShhMoney")]
    [InlineData("SleptOn")]
    [InlineData("SortedClothing")]
    [InlineData("TagMe")]
    [InlineData("Takemehome")]
    [InlineData("TheHotList")]
    [InlineData("TheJobCenter")]
    [InlineData("ThirdParty")]
    [InlineData("TrustSeal")]
    [InlineData("Wallet")]
    [InlineData("WhatWeWant")]
    [InlineData("Wolverine")]
    public void EachApiGroup_ReturnsNonEmptyWellFormedList(string apiName)
    {
        // Invoke the method by name via reflection so we can drive all APIs
        // from a single parameterised test without duplicating call sites.
        var method = typeof(TheGeekNetworkTools).GetMethod(
            apiName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method); // API method must exist

        var result = (IReadOnlyList<ToolDefinition>)method!.Invoke(null, null)!;
        Assert.NotEmpty(result);

        foreach (var tool in result)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Name),
                $"{apiName}: tool has null/empty Name.");
            Assert.StartsWith("tgn.", tool.Name, StringComparison.Ordinal);
            Assert.True(tool.Name.Count(c => c == '.') >= 2,
                $"{apiName}: tool '{tool.Name}' must be tgn.<api>.<verb>.");
            Assert.False(string.IsNullOrWhiteSpace(tool.Description),
                $"{apiName}: tool '{tool.Name}' has null/empty Description.");
            Assert.NotNull(tool.Parameters);
            Assert.NotNull(tool.RequiredParameters);

            foreach (var reqParam in tool.RequiredParameters)
            {
                Assert.True(tool.Parameters.ContainsKey(reqParam),
                    $"{apiName}.{tool.Name}: required param '{reqParam}' not in Parameters.");
            }
        }
    }

    [Fact]
    public void GetAllTools_CountMatchesSumOfAllApis()
    {
        // GetAllTools() must be the union of all individual API lists.
        // Any API added to a group method but forgotten in GetAllTools() is caught here.
        var sumFromGroups =
            TheGeekNetworkTools.Account().Count +
            TheGeekNetworkTools.Audit().Count +
            TheGeekNetworkTools.Auth().Count +
            TheGeekNetworkTools.BidBaas().Count +
            TheGeekNetworkTools.BillPayment().Count +
            TheGeekNetworkTools.Blockchain().Count +
            TheGeekNetworkTools.Butler().Count +
            TheGeekNetworkTools.CircleAether().Count +
            TheGeekNetworkTools.Ecommerce().Count +
            TheGeekNetworkTools.Electricity().Count +
            TheGeekNetworkTools.Geo().Count +
            TheGeekNetworkTools.Glocell().Count +
            TheGeekNetworkTools.Incentives().Count +
            TheGeekNetworkTools.KiffStore().Count +
            TheGeekNetworkTools.Ledger().Count +
            TheGeekNetworkTools.Localization().Count +
            TheGeekNetworkTools.Maps().Count +
            TheGeekNetworkTools.MapsData().Count +
            TheGeekNetworkTools.Media().Count +
            TheGeekNetworkTools.Messaging().Count +
            TheGeekNetworkTools.Notification().Count +
            TheGeekNetworkTools.OpSupport().Count +
            TheGeekNetworkTools.Panik().Count +
            TheGeekNetworkTools.Payfast().Count +
            TheGeekNetworkTools.Sdpkt().Count +
            TheGeekNetworkTools.ShhMoney().Count +
            TheGeekNetworkTools.SleptOn().Count +
            TheGeekNetworkTools.SortedClothing().Count +
            TheGeekNetworkTools.TagMe().Count +
            TheGeekNetworkTools.Takemehome().Count +
            TheGeekNetworkTools.TheHotList().Count +
            TheGeekNetworkTools.TheJobCenter().Count +
            TheGeekNetworkTools.ThirdParty().Count +
            TheGeekNetworkTools.TrustSeal().Count +
            TheGeekNetworkTools.Wallet().Count +
            TheGeekNetworkTools.WhatWeWant().Count +
            TheGeekNetworkTools.Wolverine().Count;

        Assert.Equal(sumFromGroups, TheGeekNetworkTools.GetAllTools().Count);
    }
}
