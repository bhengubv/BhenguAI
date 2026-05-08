using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bhengu.AI.Hosting;
using Bhengu.AI.Inference;
using Bhengu.AI.Tools;
using Xunit;

namespace Bhengu.AI.Tests;

public sealed class ButlerServiceTests : IDisposable
{
    // A real (empty) temp file is needed to pass File.Exists() in ResolveModelPathAsync.
    private readonly string _modelPath = Path.GetTempFileName();

    public void Dispose()
    {
        try { File.Delete(_modelPath); } catch { /* best-effort */ }
    }

    private ButlerService BuildService(
        string reply = "Hi!",
        string[]? streamChunks = null,
        FakeButlerObserver? observer = null,
        IToolBridge? toolBridge = null,
        bool warmOnStart = false)
    {
        var generator = new FakeChatGenerator(reply, streamChunks);
        var opts = new ButlerOptions
        {
            ModelPath    = _modelPath,
            WarmOnStart  = warmOnStart,
            Observer     = observer,
            ToolBridge   = toolBridge,
            SystemPrompt = "You are B!, a helpful on-device assistant.",
        };
        return new ButlerService(opts, generatorFactory: _ => generator);
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    [Fact]
    public async Task IsReady_BeforeStart_IsFalse()
    {
        await using var svc = BuildService();
        Assert.False(svc.IsReady);
    }

    [Fact]
    public async Task StartAsync_SetsIsReadyTrue()
    {
        await using var svc = BuildService();
        await svc.StartAsync();
        Assert.True(svc.IsReady);
    }

    [Fact]
    public async Task StartAsync_IsIdempotent()
    {
        await using var svc = BuildService();
        await svc.StartAsync();
        await svc.StartAsync(); // second call is a no-op
        Assert.True(svc.IsReady);
    }

    [Fact]
    public async Task StopAsync_SetsIsReadyFalse()
    {
        await using var svc = BuildService();
        await svc.StartAsync();
        await svc.StopAsync();
        Assert.False(svc.IsReady);
    }

    [Fact]
    public async Task DisposeAsync_ThenAsk_ThrowsObjectDisposedException()
    {
        var svc = BuildService();
        await svc.StartAsync();
        await svc.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => svc.AskAsync("hello"));
    }

    // ------------------------------------------------------------------
    // AskAsync / ChatAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task AskAsync_PrependsMissingSystemPrompt()
    {
        var generator = new FakeChatGenerator("reply");
        var opts = new ButlerOptions
        {
            ModelPath    = _modelPath,
            WarmOnStart  = false,
            SystemPrompt = "You are B!",
        };
        await using var svc = new ButlerService(opts, generatorFactory: _ => generator);
        await svc.StartAsync();

        await svc.AskAsync("tell me a joke");

        var msgs = generator.LastMessages!;
        Assert.Equal("system", msgs[0].Role);
        Assert.Equal("You are B!", msgs[0].Content);
        Assert.Equal("user", msgs[1].Role);
    }

    [Fact]
    public async Task ChatAsync_WithExistingSystemMessage_DoesNotPrepend()
    {
        var generator = new FakeChatGenerator("reply");
        var opts = new ButlerOptions
        {
            ModelPath    = _modelPath,
            WarmOnStart  = false,
            SystemPrompt = "You are B!",
        };
        await using var svc = new ButlerService(opts, generatorFactory: _ => generator);
        await svc.StartAsync();

        var messages = new List<ChatMessage>
        {
            new("system", "Custom system"),
            new("user", "hello"),
        };
        await svc.ChatAsync(messages);

        var sent = generator.LastMessages!;
        Assert.Equal(2, sent.Count);
        Assert.Equal("Custom system", sent[0].Content);
    }

    [Fact]
    public async Task ChatAsync_ReturnsGeneratorOutput()
    {
        await using var svc = BuildService(reply: "test reply");
        await svc.StartAsync();

        var result = await svc.ChatAsync(new[] { new ChatMessage("user", "hi") });
        Assert.Equal("test reply", result);
    }

    [Fact]
    public async Task ChatAsync_NullMessages_Throws()
    {
        await using var svc = BuildService();
        await svc.StartAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => svc.ChatAsync(null!));
    }

    // ------------------------------------------------------------------
    // StreamAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task StreamAsync_YieldsChunksInOrder()
    {
        var chunks = new[] { "Hello", " ", "world", "!" };
        await using var svc = BuildService(streamChunks: chunks);
        await svc.StartAsync();

        var received = new List<string>();
        await foreach (var piece in svc.StreamAsync(new[] { new ChatMessage("user", "hi") }))
            received.Add(piece);

        Assert.Equal(chunks, received);
    }

    [Fact]
    public async Task StreamAsync_CompletedEvent_HasCorrectTokenCount()
    {
        var observer = new FakeButlerObserver();
        var chunks = new[] { "a", "b", "c" };
        await using var svc = BuildService(streamChunks: chunks, observer: observer);
        await svc.StartAsync();

        // drain stream
        await foreach (var _ in svc.StreamAsync(new[] { new ChatMessage("user", "q") })) { }

        Assert.Equal(3, observer.LastStreamCompletedEvent!.TokenCount);
    }

    // ------------------------------------------------------------------
    // Tool invocation
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvokeToolAsync_NoBridge_ReturnsFailure()
    {
        await using var svc = BuildService(toolBridge: null);
        await svc.StartAsync();

        var result = await svc.InvokeToolAsync(new ToolInvocation
        {
            ToolName  = "tgn.sdpkt.get_balance",
            Arguments = new Dictionary<string, object?>(),
        });

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task InvokeToolAsync_WithBridge_DelegatesToBridge()
    {
        var expected = new ToolResult { ToolName = "tgn.sdpkt.get_balance", Success = true, Result = 42 };
        var bridge = new FakeToolBridge(expected);
        await using var svc = BuildService(toolBridge: bridge);
        await svc.StartAsync();

        var invocation = new ToolInvocation
        {
            ToolName  = "tgn.sdpkt.get_balance",
            Arguments = new Dictionary<string, object?>(),
        };
        var result = await svc.InvokeToolAsync(invocation);

        Assert.True(result.Success);
        Assert.Equal(1, bridge.InvokeCallCount);
        Assert.Same(invocation, bridge.LastInvocation);
    }

    // ------------------------------------------------------------------
    // Observer
    // ------------------------------------------------------------------

    [Fact]
    public async Task Observer_OnStartedAsync_Called()
    {
        var observer = new FakeButlerObserver();
        await using var svc = BuildService(observer: observer);
        await svc.StartAsync();

        Assert.Equal(1, observer.StartedCount);
    }

    [Fact]
    public async Task Observer_OnStoppedAsync_Called()
    {
        var observer = new FakeButlerObserver();
        await using var svc = BuildService(observer: observer);
        await svc.StartAsync();
        await svc.StopAsync();

        Assert.Equal(1, observer.StoppedCount);
    }

    [Fact]
    public async Task Observer_OnChatCompletedAsync_CalledWithResponse()
    {
        var observer = new FakeButlerObserver();
        await using var svc = BuildService(reply: "42", observer: observer);
        await svc.StartAsync();

        await svc.ChatAsync(new[] { new ChatMessage("user", "answer") });

        Assert.Equal(1, observer.ChatCompletedCount);
        Assert.Equal("42", observer.LastChatEvent!.Response);
    }

    [Fact]
    public async Task Observer_OnToolInvokedAsync_CalledAfterTool()
    {
        var observer = new FakeButlerObserver();
        var bridge = new FakeToolBridge();
        await using var svc = BuildService(observer: observer, toolBridge: bridge);
        await svc.StartAsync();

        await svc.InvokeToolAsync(new ToolInvocation
        {
            ToolName  = "fake",
            Arguments = new Dictionary<string, object?>(),
        });

        Assert.Equal(1, observer.ToolInvokedCount);
        Assert.NotNull(observer.LastToolEvent);
    }

    [Fact]
    public async Task Observer_ExceptionDoesNotPropagateToCallSite()
    {
        var observer = new FakeButlerObserver { ThrowOnNext = true };
        await using var svc = BuildService(observer: observer);

        // Must not throw, even though the observer will throw internally.
        var ex = await Record.ExceptionAsync(() => svc.StartAsync());
        Assert.Null(ex);
    }

    // ------------------------------------------------------------------
    // Concurrency
    // ------------------------------------------------------------------

    [Fact]
    public async Task ConcurrentChatAsync_BothSucceed()
    {
        await using var svc = BuildService(reply: "pong");
        await svc.StartAsync();

        var msgs = new[] { new ChatMessage("user", "ping") };

        var t1 = svc.ChatAsync(msgs);
        var t2 = svc.ChatAsync(msgs);

        var results = await Task.WhenAll(t1, t2);
        Assert.All(results, r => Assert.Equal("pong", r));
    }
}
