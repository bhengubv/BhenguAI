using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bhengu.AI.Core;
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

    // ------------------------------------------------------------------
    // AskAsync argument guards
    // ------------------------------------------------------------------

    [Fact]
    public async Task AskAsync_NullQuestion_ThrowsArgumentException()
    {
        await using var svc = BuildService();
        await svc.StartAsync();

        // ArgumentException.ThrowIfNullOrEmpty raises ArgumentNullException for null.
        await Assert.ThrowsAnyAsync<ArgumentException>(() => svc.AskAsync(null!));
    }

    [Fact]
    public async Task AskAsync_EmptyQuestion_ThrowsArgumentException()
    {
        await using var svc = BuildService();
        await svc.StartAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => svc.AskAsync(""));
    }

    // ------------------------------------------------------------------
    // Auto-start via EnsureStartedAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task ChatAsync_NotYetStarted_AutoStartsAndSucceeds()
    {
        // EnsureStartedAsync calls StartAsync if not yet started.
        await using var svc = BuildService(reply: "auto");
        Assert.False(svc.IsReady);

        var result = await svc.ChatAsync(new[] { new ChatMessage("user", "hi") });

        Assert.True(svc.IsReady);
        Assert.Equal("auto", result);
    }

    [Fact]
    public async Task AskAsync_NotYetStarted_AutoStartsAndSucceeds()
    {
        await using var svc = BuildService(reply: "auto-ask");
        var result = await svc.AskAsync("hello?");
        Assert.Equal("auto-ask", result);
    }

    // ------------------------------------------------------------------
    // StreamAsync argument guards
    // ------------------------------------------------------------------

    [Fact]
    public async Task StreamAsync_NullMessages_Throws()
    {
        await using var svc = BuildService();
        await svc.StartAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in svc.StreamAsync(null!)) { }
        });
    }

    // ------------------------------------------------------------------
    // InvokeToolAsync guards
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvokeToolAsync_NullInvocation_Throws()
    {
        await using var svc = BuildService();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            svc.InvokeToolAsync(null!));
    }

    [Fact]
    public async Task InvokeToolAsync_BeforeStart_NoBridge_ReturnsFailure()
    {
        // InvokeToolAsync does NOT require the service to be started.
        await using var svc = BuildService(toolBridge: null);

        var result = await svc.InvokeToolAsync(new ToolInvocation
        {
            ToolName  = "any.tool",
            Arguments = new Dictionary<string, object?>(),
        });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ChatAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var svc = BuildService();
        await svc.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            svc.ChatAsync(new[] { new ChatMessage("user", "hi") }));
    }

    [Fact]
    public async Task StreamAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var svc = BuildService();
        await svc.DisposeAsync();

        await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () =>
        {
            await foreach (var _ in svc.StreamAsync(new[] { new ChatMessage("user", "hi") })) { }
        });
    }

    [Fact]
    public async Task InvokeToolAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var svc = BuildService(toolBridge: new FakeToolBridge());
        await svc.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            svc.InvokeToolAsync(new ToolInvocation
            {
                ToolName  = "tgn.sdpkt.get_balance",
                Arguments = new Dictionary<string, object?>(),
            }));
    }

    [Fact]
    public async Task InvokeToolAsync_BridgeReturnsFailure_ForwardsFailureResult()
    {
        // The service must forward the bridge's failure result transparently —
        // failure is a valid ToolResult, not an exception.
        var failResult = new ToolResult
        {
            ToolName = "tgn.sdpkt.send_payment",
            Success  = false,
            Error    = "Insufficient funds.",
        };
        var bridge = new FakeToolBridge(failResult);
        await using var svc = BuildService(toolBridge: bridge);
        await svc.StartAsync();

        var result = await svc.InvokeToolAsync(new ToolInvocation
        {
            ToolName  = "tgn.sdpkt.send_payment",
            Arguments = new Dictionary<string, object?>(),
        });

        Assert.False(result.Success);
        Assert.Equal("Insufficient funds.", result.Error);
    }

    // ------------------------------------------------------------------
    // WarmOnStart
    // ------------------------------------------------------------------

    [Fact]
    public async Task WarmOnStart_True_CallsGeneratorDuringStart()
    {
        var generator = new FakeChatGenerator("warm");
        var opts = new ButlerOptions
        {
            ModelPath    = _modelPath,
            WarmOnStart  = true,
            SystemPrompt = "sys",
        };
        await using var svc = new ButlerService(opts, generatorFactory: _ => generator);
        await svc.StartAsync();

        // One call from warm-up.
        Assert.Equal(1, generator.GenerateCallCount);
    }

    [Fact]
    public async Task WarmOnStart_GeneratorThrowsNonCancelException_ServiceStartsAnyway()
    {
        // Contract: warm-up exceptions (other than OperationCanceledException) are
        // swallowed with a warning log.  The service must reach _started = true.
        var failOnce = new ThrowOnFirstCallGenerator("after-warmup-reply");
        var opts = new ButlerOptions
        {
            ModelPath    = _modelPath,
            WarmOnStart  = true,
            SystemPrompt = "sys",
        };
        await using var svc = new ButlerService(opts, generatorFactory: _ => failOnce);

        // StartAsync must NOT throw even though warm-up fails.
        var startEx = await Record.ExceptionAsync(() => svc.StartAsync());
        Assert.Null(startEx);

        // Service must be operational after recovery.
        var reply = await svc.AskAsync("hello");
        Assert.Equal("after-warmup-reply", reply);
    }

    // ------------------------------------------------------------------
    // PrepareMessages — role case-insensitivity
    // ------------------------------------------------------------------

    [Fact]
    public async Task PrepareMessages_AllCapsSystemRole_DoesNotPrepend()
    {
        // "SYSTEM" (all-caps) must satisfy the OrdinalIgnoreCase check so
        // no second system message is prepended.
        var generator = new FakeChatGenerator("reply");
        var opts = new ButlerOptions
        {
            ModelPath    = _modelPath,
            WarmOnStart  = false,
            SystemPrompt = "Injected",
        };
        await using var svc = new ButlerService(opts, generatorFactory: _ => generator);
        await svc.StartAsync();

        var messages = new List<ChatMessage>
        {
            new("SYSTEM", "Custom sys"),
            new("user", "hi"),
        };
        await svc.ChatAsync(messages);

        var sent = generator.LastMessages!;
        Assert.Equal(2, sent.Count);
        // The original role casing is preserved (PrepareMessages doesn't normalise roles).
        Assert.Equal("SYSTEM", sent[0].Role);
    }

    [Fact]
    public async Task PrepareMessages_TitleCaseSystemRole_DoesNotPrepend()
    {
        var generator = new FakeChatGenerator("reply");
        var opts = new ButlerOptions
        {
            ModelPath    = _modelPath,
            WarmOnStart  = false,
            SystemPrompt = "Injected",
        };
        await using var svc = new ButlerService(opts, generatorFactory: _ => generator);
        await svc.StartAsync();

        var messages = new List<ChatMessage>
        {
            new("System", "Custom sys"),
            new("user", "hi"),
        };
        await svc.ChatAsync(messages);

        Assert.Equal(2, generator.LastMessages!.Count);
    }

    [Fact]
    public async Task PrepareMessages_EmptySystemPrompt_DoesNotPrepend()
    {
        // When SystemPrompt is empty, PrepareMessages must not insert a
        // blank system message (guarded by string.IsNullOrEmpty check).
        var generator = new FakeChatGenerator("reply");
        var opts = new ButlerOptions
        {
            ModelPath    = _modelPath,
            WarmOnStart  = false,
            SystemPrompt = "",
        };
        await using var svc = new ButlerService(opts, generatorFactory: _ => generator);
        await svc.StartAsync();

        var messages = new[] { new ChatMessage("user", "hi") };
        await svc.ChatAsync(messages);

        var sent = Assert.Single(generator.LastMessages!);
        Assert.Equal("user", sent.Role);
    }

    // ------------------------------------------------------------------
    // Observer — stream events
    // ------------------------------------------------------------------

    [Fact]
    public async Task Observer_OnStreamStartedAsync_Called()
    {
        var observer = new FakeButlerObserver();
        var chunks   = new[] { "x", "y", "z" };
        await using var svc = BuildService(streamChunks: chunks, observer: observer);
        await svc.StartAsync();

        await foreach (var _ in svc.StreamAsync(new[] { new ChatMessage("user", "q") })) { }

        // OnStreamStartedAsync fires on the first yielded token.
        Assert.Equal(1, observer.StreamStartedCount);
    }

    // ------------------------------------------------------------------
    // StopAsync idempotency / before-start safety
    // ------------------------------------------------------------------

    [Fact]
    public async Task StopAsync_BeforeStart_DoesNotThrow()
    {
        await using var svc = BuildService();
        var ex = await Record.ExceptionAsync(() => svc.StopAsync());
        Assert.Null(ex);
        Assert.False(svc.IsReady);
    }

    [Fact]
    public async Task StopAsync_TwiceAfterStart_IsIdempotent()
    {
        await using var svc = BuildService();
        await svc.StartAsync();
        await svc.StopAsync();
        var ex = await Record.ExceptionAsync(() => svc.StopAsync());
        Assert.Null(ex);
        Assert.False(svc.IsReady);
    }

    // ------------------------------------------------------------------
    // Start → Stop → Start cycle
    // ------------------------------------------------------------------

    [Fact]
    public async Task RestartCycle_StartStopStart_ServiceBecomesReadyAgain()
    {
        await using var svc = BuildService(reply: "restarted");
        await svc.StartAsync();
        await svc.StopAsync();
        await svc.StartAsync();                // restart

        Assert.True(svc.IsReady);
        var result = await svc.ChatAsync(new[] { new ChatMessage("user", "ping") });
        Assert.Equal("restarted", result);
    }

    // ------------------------------------------------------------------
    // GenerationOptions plumbing
    // ------------------------------------------------------------------

    [Fact]
    public async Task ChatAsync_DefaultGenerationOptions_PassedToGenerator()
    {
        var customOpts = new GenerationOptions { MaxTokens = 128, Temperature = 0.1f };
        var generator  = new FakeChatGenerator("reply");
        var butlerOpts = new ButlerOptions
        {
            ModelPath              = _modelPath,
            WarmOnStart            = false,
            DefaultGenerationOptions = customOpts,
        };
        await using var svc = new ButlerService(butlerOpts, generatorFactory: _ => generator);
        await svc.StartAsync();

        await svc.ChatAsync(new[] { new ChatMessage("user", "hi") });

        Assert.Same(customOpts, generator.LastGenerateOptions);
    }

    [Fact]
    public async Task ChatAsync_CallerSuppliedOptions_OverrideDefaults()
    {
        var defaultOpts  = new GenerationOptions { MaxTokens = 128 };
        var callerOpts   = new GenerationOptions { MaxTokens = 256, Temperature = 0.9f };
        var generator    = new FakeChatGenerator("reply");
        var butlerOpts   = new ButlerOptions
        {
            ModelPath              = _modelPath,
            WarmOnStart            = false,
            DefaultGenerationOptions = defaultOpts,
        };
        await using var svc = new ButlerService(butlerOpts, generatorFactory: _ => generator);
        await svc.StartAsync();

        await svc.ChatAsync(new[] { new ChatMessage("user", "hi") }, callerOpts);

        // Caller-supplied options should win; default should NOT be used.
        Assert.Same(callerOpts, generator.LastGenerateOptions);
        Assert.NotSame(defaultOpts, generator.LastGenerateOptions);
    }

    // ------------------------------------------------------------------
    // Cancellation
    // ------------------------------------------------------------------

    [Fact]
    public async Task ChatAsync_PreCancelledToken_ThrowsOperationCancelled()
    {
        await using var svc = BuildService();
        await svc.StartAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            svc.ChatAsync(new[] { new ChatMessage("user", "hi") }, ct: cts.Token));
    }

    [Fact]
    public async Task StreamAsync_PreCancelledToken_ThrowsOperationCancelled()
    {
        await using var svc = BuildService(streamChunks: new[] { "a", "b", "c" });
        await svc.StartAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in svc.StreamAsync(
                new[] { new ChatMessage("user", "hi") }, ct: cts.Token)) { }
        });
    }

    [Fact]
    public async Task Observer_OnChatEvent_CorrelationId_IsNotEmpty()
    {
        // ButlerService must assign a fresh GUID per call — never Guid.Empty.
        var observer = new FakeButlerObserver();
        await using var svc = BuildService(reply: "hi", observer: observer);
        await svc.StartAsync();

        await svc.ChatAsync(new[] { new ChatMessage("user", "q") });

        Assert.NotEqual(Guid.Empty, observer.LastChatEvent!.CorrelationId);
    }

    [Fact]
    public async Task Observer_OnStreamCompletedAsync_CalledExactlyOnce()
    {
        var observer = new FakeButlerObserver();
        var chunks = new[] { "x", "y" };
        await using var svc = BuildService(streamChunks: chunks, observer: observer);
        await svc.StartAsync();

        await foreach (var _ in svc.StreamAsync(new[] { new ChatMessage("user", "q") })) { }

        Assert.Equal(1, observer.StreamCompletedCount);
    }

    [Fact]
    public async Task StreamAsync_DefaultGenerationOptions_PassedToGenerator()
    {
        var customOpts = new GenerationOptions { MaxTokens = 64, Temperature = 0.2f };
        var generator  = new FakeChatGenerator("r", streamChunks: new[] { "r" });
        var butlerOpts = new ButlerOptions
        {
            ModelPath              = _modelPath,
            WarmOnStart            = false,
            DefaultGenerationOptions = customOpts,
        };
        await using var svc = new ButlerService(butlerOpts, generatorFactory: _ => generator);
        await svc.StartAsync();

        await foreach (var _ in svc.StreamAsync(new[] { new ChatMessage("user", "hi") })) { }

        Assert.Same(customOpts, generator.LastStreamOptions);
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Generator that throws <see cref="InvalidOperationException"/> on its
    /// very first <see cref="IChatGenerator.GenerateAsync"/> call (simulating
    /// a warm-up failure), then returns normally on all subsequent calls.
    /// </summary>
    private sealed class ThrowOnFirstCallGenerator : IChatGenerator
    {
        private readonly string _reply;
        private int _callCount;

        public ThrowOnFirstCallGenerator(string reply = "ok") => _reply = reply;

        public Task<string> GenerateAsync(
            IReadOnlyList<ChatMessage> messages,
            GenerationOptions? options = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _callCount) == 1)
                throw new InvalidOperationException("Simulated warm-up failure.");
            return Task.FromResult(_reply);
        }

        public async IAsyncEnumerable<string> StreamAsync(
            IReadOnlyList<ChatMessage> messages,
            GenerationOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        { await Task.Yield(); yield return _reply; }

        public void Dispose() { }
    }
}

// ============================================================================
// ButlerService — model-path resolution edge cases
// (separate class because it needs its own temp-file state)
// ============================================================================

public sealed class ButlerServicePathResolutionTests
{
    // ------------------------------------------------------------------
    // ModelPath errors — caught before native load
    // ------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_ModelPathMissing_ThrowsFileNotFoundException()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gguf");
        var opts = new ButlerOptions
        {
            ModelPath   = missingPath,
            WarmOnStart = false,
        };
        await using var svc = new ButlerService(opts, generatorFactory: _ => new FakeChatGenerator());
        await Assert.ThrowsAsync<FileNotFoundException>(() => svc.StartAsync());
    }

    [Fact]
    public async Task StartAsync_NoModelPathAndNoLoader_ThrowsInvalidOperation()
    {
        // Neither ModelPath nor IModelLoader is supplied → ResolveModelPathAsync throws.
        var opts = new ButlerOptions
        {
            ModelPath   = null,  // no direct path
            WarmOnStart = false,
        };
        // No modelLoader and no generatorFactory — the factory path short-circuits
        // before the loader is needed, so we test WITHOUT a factory to ensure the
        // loader code path is exercised.
        await using var svc = new ButlerService(opts, modelLoader: null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.StartAsync());
    }

    // ------------------------------------------------------------------
    // IModelLoader path — happy path
    // ------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_WithModelLoader_ModelExists_Succeeds()
    {
        // Create a sentinel model file so FakeModelLoader.GetModelPath returns
        // a path that File.Exists() will accept.
        var tmpFile = Path.GetTempFileName();
        try
        {
            var loader = new FakeModelLoader(new Dictionary<string, string>
            {
                ["Qwen3-14B-Q4"] = tmpFile,
            });
            var opts = new ButlerOptions
            {
                // ModelPath is null — must resolve via loader
                ModelId     = "Qwen3-14B-Q4",
                WarmOnStart = false,
            };
            await using var svc = new ButlerService(opts, loader, generatorFactory: _ => new FakeChatGenerator());
            await svc.StartAsync();
            Assert.True(svc.IsReady);
        }
        finally
        {
            try { File.Delete(tmpFile); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task StartAsync_WithModelLoader_UnknownModelId_ThrowsKeyNotFound()
    {
        // FakeModelLoader throws ArgumentException for models not in its registry.
        var loader = new FakeModelLoader(); // empty registry
        var opts = new ButlerOptions
        {
            ModelId     = "Qwen3-14B-Q4",
            ModelPath   = null,
            WarmOnStart = false,
        };
        await using var svc = new ButlerService(opts, loader, generatorFactory: _ => new FakeChatGenerator());
        // FakeModelLoader.GetModelPath throws FileNotFoundException → propagates from ResolveModelPathAsync.
        await Assert.ThrowsAsync<FileNotFoundException>(() => svc.StartAsync());
    }

    [Fact]
    public async Task StartAsync_WithModelLoader_DownloadReturnsInvalidPath_ThrowsInvalidOperation()
    {
        // Contract: if DownloadModelAsync returns an empty or non-existent path
        // the service throws InvalidOperationException rather than swallowing the error.
        var badLoader = new BadPathLoader();
        var opts = new ButlerOptions
        {
            ModelId     = "Qwen3-14B-Q4",
            ModelPath   = null,
            WarmOnStart = false,
        };
        await using var svc = new ButlerService(opts, badLoader, generatorFactory: _ => new FakeChatGenerator());
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.StartAsync());
    }

    // ------------------------------------------------------------------
    // Constructor argument guard
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ButlerService(null!));
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Loader whose GetModelPath returns empty string (not cached, triggers
    /// DownloadModelAsync path) and whose DownloadModelAsync also returns an
    /// empty string — this triggers the InvalidOperationException guard in
    /// ButlerService.ResolveModelPathAsync ("Model loader returned an invalid path").
    /// </summary>
    private sealed class BadPathLoader : IModelLoader
    {
        // Return empty → service sees "not cached", proceeds to DownloadModelAsync.
        public string GetModelPath(string modelName) => "";

        public Task<string> DownloadModelAsync(
            string modelName, IProgress<float>? progress = null)
            => Task.FromResult(""); // empty path → service throws InvalidOperationException

        public bool ModelExists(string modelName) => false;
        public Task<bool> CheckForCriticalUpdateAsync() => Task.FromResult(false);
        public void Dispose() { }
    }
}

// ============================================================================
// ButlerService — DisposeAsync generator-cleanup regression test
//
// BUG: DisposeAsync sets _disposed = true, then calls StopAsync, which
// immediately returns because of its "if (_disposed) return" guard —
// so _generator?.Dispose() inside StopAsync is never reached.
// This is a PRODUCTION BLOCKER for QwenTextGenerator which holds native
// llama.cpp handles; the fix is to explicitly dispose the generator in
// DisposeAsync after StopAsync returns early.
// ============================================================================

public sealed class ButlerServiceDisposeGeneratorTests : IDisposable
{
    private readonly string _modelPath = Path.GetTempFileName();

    public void Dispose()
    {
        try { File.Delete(_modelPath); } catch { /* best-effort */ }
    }

    // Minimal generator that tracks disposal.
    private sealed class TrackingGenerator : IChatGenerator
    {
        public bool IsDisposed { get; private set; }

        public Task<string> GenerateAsync(
            IReadOnlyList<ChatMessage> messages,
            GenerationOptions? options = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult("ok");
        }

        public async IAsyncEnumerable<string> StreamAsync(
            IReadOnlyList<ChatMessage> messages,
            GenerationOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return "ok";
        }

        public void Dispose() => IsDisposed = true;
    }

    [Fact]
    public async Task DisposeAsync_WhileStarted_DisposesGenerator()
    {
        // Regression test: before the fix, DisposeAsync set _disposed = true and
        // then called StopAsync, which returned early because _disposed was true,
        // so the generator was NEVER disposed — leaking native llama.cpp handles.
        var tracking = new TrackingGenerator();
        var opts = new ButlerOptions { ModelPath = _modelPath, WarmOnStart = false };
        var svc  = new ButlerService(opts, generatorFactory: _ => tracking);

        await svc.StartAsync();
        Assert.False(tracking.IsDisposed); // sanity: not disposed yet

        await svc.DisposeAsync();

        Assert.True(tracking.IsDisposed); // generator MUST be disposed on DisposeAsync
    }

    [Fact]
    public async Task DisposeAsync_WhileNotStarted_DoesNotThrow()
    {
        var tracking = new TrackingGenerator();
        var opts = new ButlerOptions { ModelPath = _modelPath, WarmOnStart = false };
        var svc  = new ButlerService(opts, generatorFactory: _ => tracking);

        // Never started → generator is null → should not throw.
        var ex = await Record.ExceptionAsync(() => svc.DisposeAsync().AsTask());
        Assert.Null(ex);
        Assert.False(tracking.IsDisposed); // factory never called, so nothing to dispose
    }

    [Fact]
    public async Task DisposeAsync_ThenStopAsync_IsNoOp()
    {
        // After DisposeAsync the service is fully torn down; StopAsync must
        // silently return (no double-dispose, no exception).
        var tracking = new TrackingGenerator();
        var opts = new ButlerOptions { ModelPath = _modelPath, WarmOnStart = false };
        await using var svc = new ButlerService(opts, generatorFactory: _ => tracking);
        await svc.StartAsync();
        await svc.DisposeAsync();

        var ex = await Record.ExceptionAsync(() => svc.StopAsync());
        Assert.Null(ex);
    }
}
