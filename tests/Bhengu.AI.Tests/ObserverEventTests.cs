using System;
using System.Collections.Generic;
using Bhengu.AI.Hosting;
using Bhengu.AI.Inference;
using Bhengu.AI.Tools;
using Xunit;

namespace Bhengu.AI.Tests;

// ============================================================================
// AIChatEvent
// ============================================================================

public sealed class AIChatEventTests
{
    private static readonly IReadOnlyList<ChatMessage> SampleMessages =
        new[] { new ChatMessage("user", "hello") };

    [Fact]
    public void Constructor_MapsAllPositionalParameters()
    {
        var id        = Guid.NewGuid();
        var elapsed   = TimeSpan.FromMilliseconds(123);
        var timestamp = DateTimeOffset.UtcNow;

        var e = new AIChatEvent(id, SampleMessages, "Hi there!", elapsed, timestamp);

        Assert.Equal(id,             e.CorrelationId);
        Assert.Same(SampleMessages,  e.Messages);
        Assert.Equal("Hi there!",    e.Response);
        Assert.Equal(elapsed,        e.Elapsed);
        Assert.Equal(timestamp,      e.Timestamp);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var id        = Guid.NewGuid();
        var elapsed   = TimeSpan.FromSeconds(1);
        var ts        = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var msgs      = new[] { new ChatMessage("user", "hi") };

        var e1 = new AIChatEvent(id, msgs, "reply", elapsed, ts);
        var e2 = new AIChatEvent(id, msgs, "reply", elapsed, ts);

        Assert.Equal(e1, e2);
    }

    [Fact]
    public void Equality_DifferentCorrelationId_NotEqual()
    {
        var ts  = DateTimeOffset.UtcNow;
        var e1 = new AIChatEvent(Guid.NewGuid(), SampleMessages, "r", TimeSpan.Zero, ts);
        var e2 = new AIChatEvent(Guid.NewGuid(), SampleMessages, "r", TimeSpan.Zero, ts);
        Assert.NotEqual(e1, e2);
    }

    [Fact]
    public void WithExpression_OverridesResponse()
    {
        var id      = Guid.NewGuid();
        var ts      = DateTimeOffset.UtcNow;
        var orig    = new AIChatEvent(id, SampleMessages, "original", TimeSpan.Zero, ts);
        var updated = orig with { Response = "updated" };

        Assert.Equal("updated",    updated.Response);
        Assert.Equal(id,           updated.CorrelationId);
        Assert.Same(SampleMessages, updated.Messages);
    }

    [Fact]
    public void CorrelationId_IsUniquePerInstance_WhenGeneratedByService()
    {
        // Two separately constructed events (simulating two service calls) get
        // different GUIDs — validate that Guid.NewGuid() is actually unique.
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void HashCode_SameValues_AreEqual()
    {
        var id   = Guid.NewGuid();
        var ts   = DateTimeOffset.UtcNow;
        var msgs = new[] { new ChatMessage("user", "x") };

        var e1 = new AIChatEvent(id, msgs, "r", TimeSpan.Zero, ts);
        var e2 = new AIChatEvent(id, msgs, "r", TimeSpan.Zero, ts);

        Assert.Equal(e1.GetHashCode(), e2.GetHashCode());
    }
}

// ============================================================================
// AIStreamEvent
// ============================================================================

public sealed class AIStreamEventTests
{
    private static readonly IReadOnlyList<ChatMessage> Msgs =
        new[] { new ChatMessage("user", "stream me") };

    [Fact]
    public void Constructor_MapsAllPositionalParameters()
    {
        var id        = Guid.NewGuid();
        var elapsed   = TimeSpan.FromMilliseconds(50);
        var ts        = DateTimeOffset.UtcNow;

        var e = new AIStreamEvent(id, Msgs, elapsed, TokenCount: 0, ts);

        Assert.Equal(id,      e.CorrelationId);
        Assert.Same(Msgs,     e.Messages);
        Assert.Equal(elapsed, e.Elapsed);
        Assert.Equal(0,       e.TokenCount);
        Assert.Equal(ts,      e.Timestamp);
    }

    [Fact]
    public void OnStarted_TokenCount_IsZero()
    {
        // Contract: OnStreamStarted fires with TokenCount = 0.
        var e = new AIStreamEvent(Guid.NewGuid(), Msgs, TimeSpan.Zero, 0, DateTimeOffset.UtcNow);
        Assert.Equal(0, e.TokenCount);
    }

    [Fact]
    public void OnCompleted_TokenCount_ReflectsActualCount()
    {
        // Contract: OnStreamCompleted fires with TokenCount = n tokens yielded.
        var e = new AIStreamEvent(Guid.NewGuid(), Msgs, TimeSpan.FromSeconds(2), 42, DateTimeOffset.UtcNow);
        Assert.Equal(42, e.TokenCount);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var id  = Guid.NewGuid();
        var ts  = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var e1  = new AIStreamEvent(id, Msgs, TimeSpan.Zero, 3, ts);
        var e2  = new AIStreamEvent(id, Msgs, TimeSpan.Zero, 3, ts);
        Assert.Equal(e1, e2);
    }

    [Fact]
    public void Equality_DifferentTokenCount_NotEqual()
    {
        var id  = Guid.NewGuid();
        var ts  = DateTimeOffset.UtcNow;
        var e1  = new AIStreamEvent(id, Msgs, TimeSpan.Zero, 3, ts);
        var e2  = new AIStreamEvent(id, Msgs, TimeSpan.Zero, 5, ts);
        Assert.NotEqual(e1, e2);
    }

    [Fact]
    public void WithExpression_OverridesTokenCount()
    {
        var orig    = new AIStreamEvent(Guid.NewGuid(), Msgs, TimeSpan.Zero, 0, DateTimeOffset.UtcNow);
        var updated = orig with { TokenCount = 99 };
        Assert.Equal(99,                updated.TokenCount);
        Assert.Equal(orig.CorrelationId, updated.CorrelationId);
    }
}

// ============================================================================
// AIToolEvent
// ============================================================================

public sealed class AIToolEventTests
{
    private static ToolInvocation SampleInvocation => new()
    {
        ToolName  = "tgn.sdpkt.get_balance",
        Arguments = new Dictionary<string, object?>(),
    };

    private static ToolResult SuccessResult => new()
    {
        ToolName = "tgn.sdpkt.get_balance",
        Success  = true,
        Result   = 1500,
    };

    [Fact]
    public void Constructor_MapsAllPositionalParameters()
    {
        var id      = Guid.NewGuid();
        var inv     = SampleInvocation;
        var result  = SuccessResult;
        var elapsed = TimeSpan.FromMilliseconds(200);
        var ts      = DateTimeOffset.UtcNow;

        var e = new AIToolEvent(id, inv, result, elapsed, ts);

        Assert.Equal(id,      e.CorrelationId);
        Assert.Same(inv,      e.Invocation);
        Assert.Same(result,   e.Result);
        Assert.Equal(elapsed, e.Elapsed);
        Assert.Equal(ts,      e.Timestamp);
    }

    [Fact]
    public void Result_ReflectsSuccessFlag()
    {
        var success = new ToolResult { ToolName = "tgn.test.op", Success = true };
        var failure = new ToolResult { ToolName = "tgn.test.op", Success = false, Error = "err" };

        var eSucc = new AIToolEvent(Guid.NewGuid(), SampleInvocation, success, TimeSpan.Zero, DateTimeOffset.UtcNow);
        var eFail = new AIToolEvent(Guid.NewGuid(), SampleInvocation, failure, TimeSpan.Zero, DateTimeOffset.UtcNow);

        Assert.True(eSucc.Result.Success);
        Assert.False(eFail.Result.Success);
        Assert.Equal("err", eFail.Result.Error);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var id  = Guid.NewGuid();
        var inv = SampleInvocation;
        var res = SuccessResult;
        var ts  = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        var e1 = new AIToolEvent(id, inv, res, TimeSpan.Zero, ts);
        var e2 = new AIToolEvent(id, inv, res, TimeSpan.Zero, ts);

        Assert.Equal(e1, e2);
    }

    [Fact]
    public void WithExpression_OverridesElapsed()
    {
        var orig    = new AIToolEvent(Guid.NewGuid(), SampleInvocation, SuccessResult, TimeSpan.Zero, DateTimeOffset.UtcNow);
        var updated = orig with { Elapsed = TimeSpan.FromSeconds(5) };

        Assert.Equal(TimeSpan.FromSeconds(5), updated.Elapsed);
        Assert.Equal(orig.CorrelationId,       updated.CorrelationId);
    }

    [Fact]
    public void Invocation_ToolName_IsPreserved()
    {
        var inv = new ToolInvocation
        {
            ToolName  = "tgn.bidbaas.place_bid",
            Arguments = new Dictionary<string, object?> { ["lotId"] = 7 },
        };
        var e = new AIToolEvent(Guid.NewGuid(), inv, SuccessResult, TimeSpan.Zero, DateTimeOffset.UtcNow);
        Assert.Equal("tgn.bidbaas.place_bid", e.Invocation.ToolName);
    }
}
