// AIServiceV2Tests.cs
//
// Tests for v2.0 capabilities:
//   - ParseToolCall (static internal helper)
//   - AgenticChatAsync (agentic loop with tool execution)
//   - SubmitFeedbackAsync
//   - Context enrichment: device context, RAG, persona hints

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Circle.AI.Core;
using Circle.AI.Hosting;
using Circle.AI.Inference;
using Circle.AI.Memory;
using Circle.AI.Tools;
using Xunit;

namespace Circle.AI.Tests;

// ============================================================================
// ParseToolCall (internal static)
// ============================================================================

public sealed class ParseToolCallTests
{
    [Fact]
    public void NullInput_ReturnsNull()
        => Assert.Null(AIService.ParseToolCall(null!));

    [Fact]
    public void EmptyInput_ReturnsNull()
        => Assert.Null(AIService.ParseToolCall(""));

    [Fact]
    public void PlainText_ReturnsNull()
        => Assert.Null(AIService.ParseToolCall("Here is your answer."));

    [Fact]
    public void ValidTag_NameField_ParsesToolName()
    {
        const string response =
            "Thinking…\n" +
            "<tool_call>{\"name\":\"tgn.sdpkt.get_balance\",\"arguments\":{}}</tool_call>";

        var inv = AIService.ParseToolCall(response);

        Assert.NotNull(inv);
        Assert.Equal("tgn.sdpkt.get_balance", inv!.ToolName);
        Assert.Empty(inv.Arguments);
    }

    [Fact]
    public void ValidTag_ToolNameField_ParsesAlternateSpelling()
    {
        const string response =
            "<tool_call>{\"tool_name\":\"tgn.panik.trigger_sos\",\"arguments\":{}}</tool_call>";

        var inv = AIService.ParseToolCall(response);

        Assert.NotNull(inv);
        Assert.Equal("tgn.panik.trigger_sos", inv!.ToolName);
    }

    [Fact]
    public void ValidTag_WithArguments_ParsesArguments()
    {
        const string response =
            "<tool_call>{\"name\":\"tgn.bidbaas.place_bid\",\"arguments\":{\"auction_id\":\"abc123\",\"amount\":\"500\"}}</tool_call>";

        var inv = AIService.ParseToolCall(response);

        Assert.NotNull(inv);
        Assert.Equal("tgn.bidbaas.place_bid", inv!.ToolName);
        Assert.True(inv.Arguments.ContainsKey("auction_id"));
        Assert.True(inv.Arguments.ContainsKey("amount"));
    }

    [Fact]
    public void MalformedJson_ReturnsNull()
    {
        const string response = "<tool_call>not valid json</tool_call>";
        Assert.Null(AIService.ParseToolCall(response));
    }

    [Fact]
    public void MissingCloseTag_ReturnsNull()
    {
        const string response = "<tool_call>{\"name\":\"foo\"}";
        Assert.Null(AIService.ParseToolCall(response));
    }

    [Fact]
    public void EmptyJsonObject_ReturnsNull()
    {
        const string response = "<tool_call>{}</tool_call>";
        Assert.Null(AIService.ParseToolCall(response));
    }
}

// ============================================================================
// AgenticChatAsync
// ============================================================================

public sealed class AgenticChatTests : IDisposable
{
    // A real (empty) temp file is required so ResolveModelPathAsync passes File.Exists().
    private readonly string _modelPath = System.IO.Path.GetTempFileName();

    public void Dispose()
    {
        try { System.IO.File.Delete(_modelPath); } catch { /* best-effort */ }
    }

    private AIOptions Opts(
        IChatGenerator? generator = null,
        IToolBridge? bridge = null,
        int maxIter = 5,
        IEpisodicMemoryStore? memory = null)
        => new()
        {
            ModelPath             = _modelPath,
            WarmOnStart           = false,
            SystemPrompt          = "sys",
            AgenticMaxIterations  = maxIter,
            ToolBridge            = bridge,
            EpisodicMemory        = memory,
        };

    // ------------------------------------------------------------------
    // Argument guards
    // ------------------------------------------------------------------

    [Fact]
    public async Task AgenticChatAsync_NullPrompt_Throws()
    {
        await using var svc = new AIService(
            Opts(), generatorFactory: _ => new FakeChatGenerator());

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            svc.AgenticChatAsync(null!));
    }

    [Fact]
    public async Task AgenticChatAsync_EmptyPrompt_Throws()
    {
        await using var svc = new AIService(
            Opts(), generatorFactory: _ => new FakeChatGenerator());

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            svc.AgenticChatAsync(""));
    }

    // ------------------------------------------------------------------
    // No tool call path — single response returned immediately
    // ------------------------------------------------------------------

    [Fact]
    public async Task AgenticChatAsync_NoToolCall_ReturnsSingleResponse()
    {
        var gen = new FakeChatGenerator("The answer is 42.");
        await using var svc = new AIService(
            Opts(), generatorFactory: _ => gen);

        var result = await svc.AgenticChatAsync("What is the answer?");

        Assert.Equal("The answer is 42.", result);
        Assert.Equal(1, gen.GenerateCallCount);
    }

    // ------------------------------------------------------------------
    // Tool call path — loops and executes tool
    // ------------------------------------------------------------------

    [Fact]
    public async Task AgenticChatAsync_WithToolCall_ExecutesToolAndContinues()
    {
        var bridge = new FakeToolBridge(
            new ToolResult { ToolName = "tgn.test.ping", Success = true, Result = "pong" });

        var gen = new AgenticFakeChatGenerator(
            toolName: "tgn.test.ping",
            finalAnswer: "Tool returned: pong");

        await using var svc = new AIService(
            Opts(bridge: bridge), generatorFactory: _ => gen);

        var result = await svc.AgenticChatAsync("run the ping tool");

        Assert.Equal("Tool returned: pong", result);
        Assert.Equal(1, bridge.InvokeCallCount);
    }

    [Fact]
    public async Task AgenticChatAsync_ToolCallNobridge_AppendsErrorAndContinues()
    {
        // AgenticFakeChatGenerator emits tool call on first call, plain on second.
        var gen = new AgenticFakeChatGenerator(finalAnswer: "Fallback answer.");

        await using var svc = new AIService(
            Opts(bridge: null), generatorFactory: _ => gen); // no bridge

        // Should not throw — returns the last (non-tool-call) response.
        var result = await svc.AgenticChatAsync("do something");

        // The loop breaks after the no-bridge error and returns the second response.
        Assert.Equal("Fallback answer.", result);
    }

    // ------------------------------------------------------------------
    // MaxIterations cap
    // ------------------------------------------------------------------

    [Fact]
    public async Task AgenticChatAsync_MaxIterationsReached_DoesNotThrow()
    {
        // Generator always returns a tool call so the loop keeps firing.
        var alwaysToolCall = new AlwaysToolCallGenerator("tgn.test.noop", "not done");
        var bridge = new FakeToolBridge(
            new ToolResult { ToolName = "tgn.test.noop", Success = true, Result = "ok" });

        await using var svc = new AIService(
            Opts(bridge: bridge, maxIter: 3), generatorFactory: _ => alwaysToolCall);

        var result = await svc.AgenticChatAsync("loop forever");

        // Must return something (the last tool-call response) and not hang.
        Assert.NotNull(result);
    }

    // ------------------------------------------------------------------
    // Episodic store write
    // ------------------------------------------------------------------

    [Fact]
    public async Task AgenticChatAsync_WritesToEpisodicStore()
    {
        var memory = new InMemoryEpisodicStore();
        var gen = new FakeChatGenerator("agentic result");

        await using var svc = new AIService(
            Opts(memory: memory), generatorFactory: _ => gen);

        await svc.AgenticChatAsync("store this please");

        // Give the fire-and-forget store task time to complete.
        await Task.Delay(100);

        Assert.Equal(1, await memory.CountAsync());
    }

    // ------------------------------------------------------------------
    // Helper — generator that always returns a tool call
    // ------------------------------------------------------------------

    private sealed class AlwaysToolCallGenerator : IChatGenerator
    {
        private readonly string _toolName;
        private readonly string _response;

        public AlwaysToolCallGenerator(string toolName, string response)
        {
            _toolName = toolName;
            _response = response;
        }

        public Task<string> GenerateAsync(
            IReadOnlyList<ChatMessage> messages,
            GenerationOptions? options = null,
            CancellationToken ct = default)
            => Task.FromResult(
                $"<tool_call>{{\"name\":\"{_toolName}\",\"arguments\":{{}}}}</tool_call>");

        public async System.Collections.Generic.IAsyncEnumerable<string> StreamAsync(
            IReadOnlyList<ChatMessage> messages,
            GenerationOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return _response;
        }

        public void Dispose() { }
    }
}

// ============================================================================
// SubmitFeedbackAsync
// ============================================================================

public sealed class SubmitFeedbackTests
{
    private static readonly string _modelPath = "fake.gguf";

    [Fact]
    public async Task SubmitFeedbackAsync_NullSignal_Throws()
    {
        await using var svc = new AIService(
            new AIOptions { ModelPath = _modelPath, WarmOnStart = false },
            generatorFactory: _ => new FakeChatGenerator());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            svc.SubmitFeedbackAsync(null!));
    }

    [Fact]
    public async Task SubmitFeedbackAsync_NoStore_IsNoOp()
    {
        await using var svc = new AIService(
            new AIOptions { ModelPath = _modelPath, WarmOnStart = false },
            generatorFactory: _ => new FakeChatGenerator());

        // Must not throw when FeedbackStore is null.
        var ex = await Record.ExceptionAsync(() =>
            svc.SubmitFeedbackAsync(new FeedbackSignal
            {
                UserText      = "q",
                AssistantText = "a",
                Polarity      = FeedbackPolarity.Positive,
            }));
        Assert.Null(ex);
    }

    [Fact]
    public async Task SubmitFeedbackAsync_WithStore_SignalIsPersisted()
    {
        var feedbackStore = new InMemoryFeedbackStore();
        await using var svc = new AIService(
            new AIOptions
            {
                ModelPath    = _modelPath,
                WarmOnStart  = false,
                FeedbackStore = feedbackStore,
            },
            generatorFactory: _ => new FakeChatGenerator());

        await svc.SubmitFeedbackAsync(new FeedbackSignal
        {
            UserText      = "Was this helpful?",
            AssistantText = "Yes.",
            Polarity      = FeedbackPolarity.Positive,
        });

        Assert.Equal(1, await feedbackStore.CountAsync());
    }

    [Fact]
    public async Task SubmitFeedbackAsync_PositiveSignal_IncrementsPersonaCounter()
    {
        var personaStore = new InMemoryPersonaStore();
        await using var svc = new AIService(
            new AIOptions
            {
                ModelPath     = _modelPath,
                WarmOnStart   = false,
                FeedbackStore = new InMemoryFeedbackStore(),
                PersonaStore  = personaStore,
                PersonaUserId = "test-user",
            },
            generatorFactory: _ => new FakeChatGenerator());

        await svc.SubmitFeedbackAsync(new FeedbackSignal
        {
            Polarity = FeedbackPolarity.Positive,
        });

        // Save persona and reload to verify counter was written.
        await svc.StopAsync();
        var p = await personaStore.LoadAsync("test-user");
        Assert.Equal(1, p.PositiveSignals);
    }
}

// ============================================================================
// Context enrichment — device context
// ============================================================================

public sealed class ContextEnrichmentTests : IDisposable
{
    // A real (empty) temp file is required so ResolveModelPathAsync passes File.Exists().
    private readonly string _modelPath = System.IO.Path.GetTempFileName();

    public void Dispose()
    {
        try { System.IO.File.Delete(_modelPath); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task ChatAsync_WithBatteryContext_InjectsBatteryIntoSystemPrompt()
    {
        var ctx = new FakeDeviceContext
        {
            BatteryLevel = 0.15f,
            IsCharging   = false,
            NetworkType  = "wifi",
        };
        var gen = new CapturingChatGenerator("ok");

        await using var svc = new AIService(
            new AIOptions
            {
                ModelPath    = _modelPath,
                WarmOnStart  = false,
                DeviceContext = ctx,
            },
            generatorFactory: _ => gen);

        await svc.AskAsync("what's my battery?");

        Assert.Single(gen.CapturedSystemMessages);
        var sys = gen.CapturedSystemMessages[0]!;
        Assert.Contains("Battery: 15%", sys);
    }

    [Fact]
    public async Task ChatAsync_WithActiveApp_InjectsAppId()
    {
        var ctx = new FakeDeviceContext { ActiveAppId = "tgn.bidbaas" };
        var gen = new CapturingChatGenerator("ok");

        await using var svc = new AIService(
            new AIOptions
            {
                ModelPath    = _modelPath,
                WarmOnStart  = false,
                DeviceContext = ctx,
            },
            generatorFactory: _ => gen);

        await svc.AskAsync("am I in BidBaas?");

        var sys = gen.CapturedSystemMessages[0]!;
        Assert.Contains("tgn.bidbaas", sys);
    }

    [Fact]
    public async Task ChatAsync_NullDeviceContext_NoContextBlock()
    {
        var gen = new CapturingChatGenerator("ok");

        await using var svc = new AIService(
            new AIOptions
            {
                ModelPath   = _modelPath,
                WarmOnStart = false,
                // DeviceContext = null (default)
            },
            generatorFactory: _ => gen);

        await svc.AskAsync("hello");

        var sys = gen.CapturedSystemMessages[0]!;
        Assert.DoesNotContain("[Device context]", sys);
    }

    // ------------------------------------------------------------------
    // RAG context
    // ------------------------------------------------------------------

    [Fact]
    public async Task ChatAsync_WithMemoryAndPriorExchange_InjectsRagBlock()
    {
        var memory = new InMemoryEpisodicStore();
        await memory.AddAsync(new EpisodicMemoryEntry
        {
            UserText      = "What is SDPKT?",
            AssistantText = "SDPKT is the digital wallet.",
        });

        var gen = new CapturingChatGenerator("ok");

        await using var svc = new AIService(
            new AIOptions
            {
                ModelPath      = _modelPath,
                WarmOnStart    = false,
                EpisodicMemory = memory,
                RagTopK        = 3,
            },
            generatorFactory: _ => gen);

        await svc.AskAsync("tell me about the wallet");

        var sys = gen.CapturedSystemMessages[0]!;
        Assert.Contains("[Relevant past exchanges", sys);
        Assert.Contains("SDPKT", sys);
    }

    [Fact]
    public async Task ChatAsync_StoresExchangeInEpisodicMemory()
    {
        var memory = new InMemoryEpisodicStore();
        var gen = new FakeChatGenerator("stored reply");

        await using var svc = new AIService(
            new AIOptions
            {
                ModelPath      = _modelPath,
                WarmOnStart    = false,
                EpisodicMemory = memory,
            },
            generatorFactory: _ => gen);

        await svc.AskAsync("remember this");

        // Give the fire-and-forget store task a moment.
        await Task.Delay(100);

        Assert.Equal(1, await memory.CountAsync());
        var recent = await memory.GetRecentAsync(1);
        Assert.Equal("stored reply", recent[0].AssistantText);
    }

    // ------------------------------------------------------------------
    // Persona enrichment
    // ------------------------------------------------------------------

    [Fact]
    public async Task ChatAsync_WithBriefPersona_InjectsPersonaHint()
    {
        var personaStore = new InMemoryPersonaStore();
        var persona = await personaStore.LoadAsync("test-user");
        persona.Verbosity = "brief";
        await personaStore.SaveAsync(persona);

        var gen = new CapturingChatGenerator("ok");

        await using var svc = new AIService(
            new AIOptions
            {
                ModelPath     = _modelPath,
                WarmOnStart   = false,
                PersonaStore  = personaStore,
                PersonaUserId = "test-user",
            },
            generatorFactory: _ => gen);

        await svc.AskAsync("what's up?");

        var sys = gen.CapturedSystemMessages[0]!;
        Assert.Contains("brief", sys, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisposeAsync_SavesPersona()
    {
        var personaStore = new InMemoryPersonaStore();
        var gen = new FakeChatGenerator("ok");

        var svc = new AIService(
            new AIOptions
            {
                ModelPath     = _modelPath,
                WarmOnStart   = false,
                FeedbackStore = new InMemoryFeedbackStore(),
                PersonaStore  = personaStore,
                PersonaUserId = "persist-user",
            },
            generatorFactory: _ => gen);

        // Trigger a positive feedback to dirty the persona state.
        await svc.SubmitFeedbackAsync(new FeedbackSignal
        {
            Polarity = FeedbackPolarity.Positive,
        });

        await svc.DisposeAsync();

        var saved = await personaStore.LoadAsync("persist-user");
        Assert.Equal(1, saved.PositiveSignals);
    }
}
