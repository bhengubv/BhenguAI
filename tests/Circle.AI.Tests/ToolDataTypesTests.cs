using System.Collections.Generic;
using Circle.AI.Tools;
using Xunit;

namespace Circle.AI.Tests;

// ============================================================================
// ToolDefinition
// ============================================================================

public sealed class ToolDefinitionTests
{
    private static IReadOnlyDictionary<string, ToolParameter> EmptyParams =>
        new Dictionary<string, ToolParameter>();

    private static IReadOnlyList<string> NoRequired =>
        System.Array.Empty<string>();

    [Fact]
    public void Properties_SetViaInit_AreReflected()
    {
        var def = new ToolDefinition
        {
            Name               = "tgn.test.do_thing",
            Description        = "Does the thing.",
            Parameters         = EmptyParams,
            RequiredParameters = NoRequired,
        };

        Assert.Equal("tgn.test.do_thing", def.Name);
        Assert.Equal("Does the thing.",   def.Description);
        Assert.Empty(def.Parameters);
        Assert.Empty(def.RequiredParameters);
    }

    [Fact]
    public void Parameters_WithEntries_AreAccessible()
    {
        var param = new ToolParameter { Type = "string", Description = "An ID." };
        var def = new ToolDefinition
        {
            Name               = "tgn.test.do_thing",
            Description        = "A tool.",
            Parameters         = new Dictionary<string, ToolParameter> { ["id"] = param },
            RequiredParameters = new[] { "id" },
        };

        Assert.True(def.Parameters.ContainsKey("id"));
        Assert.Same(param, def.Parameters["id"]);
        var reqd = Assert.Single(def.RequiredParameters);
        Assert.Equal("id", reqd);
    }

    [Fact]
    public void RequiredParameters_MultipleEntries_AllReflected()
    {
        var def = new ToolDefinition
        {
            Name               = "tgn.test.multi",
            Description        = "Multi-param tool.",
            Parameters         = new Dictionary<string, ToolParameter>
            {
                ["a"] = new() { Type = "string",  Description = "param a" },
                ["b"] = new() { Type = "integer", Description = "param b" },
            },
            RequiredParameters = new[] { "a", "b" },
        };

        Assert.Equal(2, def.RequiredParameters.Count);
        Assert.Contains("a", def.RequiredParameters);
        Assert.Contains("b", def.RequiredParameters);
    }

    [Fact]
    public void NameStartsWithTgn_PatternValidation()
    {
        var def = new ToolDefinition
        {
            Name               = "tgn.sdpkt.get_balance",
            Description        = "Get balance.",
            Parameters         = EmptyParams,
            RequiredParameters = NoRequired,
        };
        Assert.StartsWith("tgn.", def.Name, System.StringComparison.Ordinal);
    }
}

// ============================================================================
// ToolParameter
// ============================================================================

public sealed class ToolParameterTests
{
    [Fact]
    public void Properties_BasicTypes_AreReflected()
    {
        var p = new ToolParameter { Type = "string", Description = "User ID." };
        Assert.Equal("string",  p.Type);
        Assert.Equal("User ID.", p.Description);
        Assert.Null(p.Enum);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("number")]
    [InlineData("boolean")]
    [InlineData("object")]
    [InlineData("array")]
    [InlineData("integer")]
    public void Type_CommonTypes_AreAccepted(string type)
    {
        var p = new ToolParameter { Type = type, Description = "desc" };
        Assert.Equal(type, p.Type);
    }

    [Fact]
    public void Enum_WhenSet_IsReflected()
    {
        var p = new ToolParameter
        {
            Type        = "string",
            Description = "Direction.",
            Enum        = new[] { "north", "south", "east", "west" },
        };

        Assert.NotNull(p.Enum);
        Assert.Equal(4,       p.Enum!.Length);
        Assert.Contains("north", p.Enum);
    }

    [Fact]
    public void Enum_WhenNotSet_IsNull()
    {
        var p = new ToolParameter { Type = "string", Description = "free text" };
        Assert.Null(p.Enum);
    }
}

// ============================================================================
// ToolInvocation
// ============================================================================

public sealed class ToolInvocationTests
{
    [Fact]
    public void Properties_SetViaInit_AreReflected()
    {
        var args = new Dictionary<string, object?> { ["amount"] = 100 };
        var inv = new ToolInvocation
        {
            ToolName  = "tgn.sdpkt.send_payment",
            Arguments = args,
        };

        Assert.Equal("tgn.sdpkt.send_payment", inv.ToolName);
        Assert.Same(args, inv.Arguments);
    }

    [Fact]
    public void Arguments_EmptyDictionary_IsAllowed()
    {
        var inv = new ToolInvocation
        {
            ToolName  = "tgn.auth.request_otp",
            Arguments = new Dictionary<string, object?>(),
        };

        Assert.Empty(inv.Arguments);
    }

    [Fact]
    public void Arguments_NullValues_AreAllowed()
    {
        // Tool arguments may legitimately be null (optional params not provided).
        var inv = new ToolInvocation
        {
            ToolName  = "tgn.test.nullable",
            Arguments = new Dictionary<string, object?> { ["optional"] = null },
        };

        Assert.True(inv.Arguments.ContainsKey("optional"));
        Assert.Null(inv.Arguments["optional"]);
    }

    [Fact]
    public void Arguments_MixedTypes_ArePreserved()
    {
        var inv = new ToolInvocation
        {
            ToolName  = "tgn.test.mixed",
            Arguments = new Dictionary<string, object?>
            {
                ["text"]   = "hello",
                ["number"] = 42,
                ["flag"]   = true,
            },
        };

        Assert.Equal("hello", inv.Arguments["text"]);
        Assert.Equal(42,      inv.Arguments["number"]);
        Assert.Equal(true,    inv.Arguments["flag"]);
    }
}

// ============================================================================
// ToolResult
// ============================================================================

public sealed class ToolResultTests
{
    [Fact]
    public void SuccessResult_PropertiesAreReflected()
    {
        var r = new ToolResult
        {
            ToolName = "tgn.sdpkt.get_balance",
            Success  = true,
            Result   = 1500.00m,
        };

        Assert.Equal("tgn.sdpkt.get_balance", r.ToolName);
        Assert.True(r.Success);
        Assert.Equal(1500.00m, r.Result);
        Assert.Null(r.Error);
    }

    [Fact]
    public void FailureResult_HasErrorMessage()
    {
        var r = new ToolResult
        {
            ToolName = "tgn.sdpkt.get_balance",
            Success  = false,
            Error    = "Insufficient permissions.",
        };

        Assert.False(r.Success);
        Assert.Equal("Insufficient permissions.", r.Error);
        Assert.Null(r.Result);
    }

    [Fact]
    public void Result_NullValue_IsAllowed()
    {
        // A tool may succeed but return no payload (void operation).
        var r = new ToolResult
        {
            ToolName = "tgn.panik.cancel_sos",
            Success  = true,
            Result   = null,
        };

        Assert.True(r.Success);
        Assert.Null(r.Result);
    }

    [Fact]
    public void Result_CanBeString()
    {
        var r = new ToolResult
        {
            ToolName = "tgn.auth.request_otp",
            Success  = true,
            Result   = "OTP sent to +27820000000",
        };

        Assert.Equal("OTP sent to +27820000000", r.Result);
    }

    [Fact]
    public void Result_CanBeComplexObject()
    {
        var payload = new { balance = 100, currency = "ZAR" };
        var r = new ToolResult
        {
            ToolName = "tgn.sdpkt.get_balance",
            Success  = true,
            Result   = payload,
        };

        Assert.Same(payload, r.Result);
    }

    [Fact]
    public void ErrorAndResult_BothNull_IsValidForNoContentSuccess()
    {
        // Some tools return Success=true with no payload and no error.
        var r = new ToolResult
        {
            ToolName = "tgn.panik.cancel_sos",
            Success  = true,
        };

        Assert.True(r.Success);
        Assert.Null(r.Result);
        Assert.Null(r.Error);
    }

    [Fact]
    public void ToolName_IsPreservedInFailure()
    {
        // The bridge always echos the ToolName back so callers can correlate responses.
        var r = new ToolResult
        {
            ToolName = "tgn.bidbaas.place_bid",
            Success  = false,
            Error    = "Auction closed.",
        };

        Assert.Equal("tgn.bidbaas.place_bid", r.ToolName);
    }
}
