// Shared test doubles — no mocking libraries, no external deps.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Circle.AI.Core;
using Circle.AI.Embeddings;
using Circle.AI.Hosting;
using Circle.AI.Inference;
using Circle.AI.Memory;
using Circle.AI.Tools;
using Circle.AI.Voice;

namespace Circle.AI.Tests;

// ---------------------------------------------------------------------------
// IModelLoader
// ---------------------------------------------------------------------------

/// <summary>
/// In-memory model loader with configurable behaviour for tests.
/// </summary>
internal sealed class FakeModelLoader : IModelLoader
{
    private readonly Dictionary<string, string> _paths;
    private bool _disposed;

    /// <param name="paths">Map of modelName → local path to return from GetModelPath/DownloadModelAsync.</param>
    public FakeModelLoader(Dictionary<string, string>? paths = null)
    {
        _paths = paths ?? new Dictionary<string, string>();
    }

    public int DownloadCallCount { get; private set; }

    public Task<string> DownloadModelAsync(string modelName, IProgress<float>? progress = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FakeModelLoader));
        DownloadCallCount++;
        if (_paths.TryGetValue(modelName, out var path))
            return Task.FromResult(path);
        throw new ArgumentException($"Model '{modelName}' not found in fake registry.");
    }

    public string GetModelPath(string modelName)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FakeModelLoader));
        if (_paths.TryGetValue(modelName, out var path))
            return path;
        throw new FileNotFoundException($"Model '{modelName}' not found.");
    }

    public bool ModelExists(string modelName) =>
        !_disposed && _paths.ContainsKey(modelName) && File.Exists(_paths[modelName]);

    public Task<bool> CheckForCriticalUpdateAsync() => Task.FromResult(false);

    public void Dispose() { _disposed = true; }
}

// ---------------------------------------------------------------------------
// IChatGenerator
// ---------------------------------------------------------------------------

/// <summary>
/// Fake generator that returns a canned reply and records calls for assertion.
/// </summary>
internal sealed class FakeChatGenerator : IChatGenerator
{
    private readonly string _reply;
    private readonly string[] _streamChunks;

    public FakeChatGenerator(string reply = "Hello!", string[]? streamChunks = null)
    {
        _reply = reply;
        _streamChunks = streamChunks ?? new[] { "Hel", "lo", "!" };
    }

    public int GenerateCallCount { get; private set; }
    public int StreamCallCount { get; private set; }
    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

    /// <summary>The <see cref="GenerationOptions"/> passed to the most recent GenerateAsync call.</summary>
    public GenerationOptions? LastGenerateOptions { get; private set; }

    /// <summary>The <see cref="GenerationOptions"/> passed to the most recent StreamAsync call.</summary>
    public GenerationOptions? LastStreamOptions { get; private set; }

    public Task<string> GenerateAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        GenerateCallCount++;
        LastMessages = messages;
        LastGenerateOptions = options;
        return Task.FromResult(_reply);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        StreamCallCount++;
        LastMessages = messages;
        LastStreamOptions = options;
        foreach (var chunk in _streamChunks)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return chunk;
        }
    }

    public void Dispose() { /* no-op — fake has no native resources */ }
}

// ---------------------------------------------------------------------------
// IToolBridge
// ---------------------------------------------------------------------------

internal sealed class FakeToolBridge : IToolBridge
{
    private readonly ToolResult _result;

    public FakeToolBridge(ToolResult? result = null)
    {
        _result = result ?? new ToolResult { ToolName = "fake", Success = true, Result = "ok" };
    }

    public IReadOnlyList<ToolDefinition> AvailableTools =>
        new[] { new ToolDefinition { Name = "fake", Description = "Fake tool", Parameters = new Dictionary<string, ToolParameter>(), RequiredParameters = Array.Empty<string>() } };

    public int InvokeCallCount { get; private set; }
    public ToolInvocation? LastInvocation { get; private set; }

    public Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken ct = default)
    {
        InvokeCallCount++;
        LastInvocation = invocation;
        return Task.FromResult(_result);
    }
}

// ---------------------------------------------------------------------------
// IAIObserver
// ---------------------------------------------------------------------------

internal sealed class FakeButlerObserver : IAIObserver
{
    public int StartedCount { get; private set; }
    public int StoppedCount { get; private set; }
    public int ChatCompletedCount { get; private set; }
    public int StreamStartedCount { get; private set; }
    public int StreamCompletedCount { get; private set; }
    public int ToolInvokedCount { get; private set; }

    public AIChatEvent? LastChatEvent { get; private set; }
    public AIStreamEvent? LastStreamStartedEvent { get; private set; }
    public AIStreamEvent? LastStreamCompletedEvent { get; private set; }
    public AIToolEvent? LastToolEvent { get; private set; }

    /// <summary>When true the observer throws, to test isolation.</summary>
    public bool ThrowOnNext { get; set; }

    public ValueTask OnStartedAsync(CancellationToken ct = default)
    {
        StartedCount++;
        if (ThrowOnNext) throw new InvalidOperationException("Observer intentional throw");
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStoppedAsync(CancellationToken ct = default)
    {
        StoppedCount++;
        return ValueTask.CompletedTask;
    }

    public ValueTask OnChatCompletedAsync(AIChatEvent @event, CancellationToken ct = default)
    {
        ChatCompletedCount++;
        LastChatEvent = @event;
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStreamStartedAsync(AIStreamEvent @event, CancellationToken ct = default)
    {
        StreamStartedCount++;
        LastStreamStartedEvent = @event;
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStreamCompletedAsync(AIStreamEvent @event, CancellationToken ct = default)
    {
        StreamCompletedCount++;
        LastStreamCompletedEvent = @event;
        return ValueTask.CompletedTask;
    }

    public ValueTask OnToolInvokedAsync(AIToolEvent @event, CancellationToken ct = default)
    {
        ToolInvokedCount++;
        LastToolEvent = @event;
        return ValueTask.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// IWakeWordDetector
// ---------------------------------------------------------------------------

internal sealed class FakeWakeWordDetector : IWakeWordDetector
{
    public string WakeWord => "hey b";
    public bool IsListening { get; private set; }

    public event EventHandler<WakeWordDetectedEventArgs>? WakeWordDetected;

    public int StartCallCount { get; private set; }
    public int StopCallCount { get; private set; }

    public Task StartAsync(CancellationToken ct = default)
    {
        StartCallCount++;
        IsListening = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        StopCallCount++;
        IsListening = false;
        return Task.CompletedTask;
    }

    /// <summary>Fires the wake event so tests can trigger pipeline activations.</summary>
    public void FireWakeWord(string keyword = "hey b")
        => WakeWordDetected?.Invoke(this, new WakeWordDetectedEventArgs
        {
            WakeWord   = keyword,
            Confidence = 0.99f,
        });

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

// ---------------------------------------------------------------------------
// IVoiceTranscriber
// ---------------------------------------------------------------------------

internal sealed class FakeVoiceTranscriber : IVoiceTranscriber
{
    private readonly TranscriptionResult _result;
    private readonly bool _shouldThrow;

    public FakeVoiceTranscriber(string text = "test transcription", bool shouldThrow = false)
    {
        _result = new TranscriptionResult(text, 0.95f, "en");
        _shouldThrow = shouldThrow;
    }

    public int TranscribeCallCount { get; private set; }

    public async IAsyncEnumerable<PartialTranscription> StreamTranscribeAsync(
        IAsyncEnumerable<ReadOnlyMemory<byte>> audioChunks,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        TranscribeCallCount++;
        // Drain the audio stream
        await foreach (var _ in audioChunks.WithCancellation(ct).ConfigureAwait(false)) { }
        if (_shouldThrow)
            throw new InvalidOperationException("Simulated transcriber failure");
        // PartialTranscription(Text, IsFinal, Confidence)
        yield return new PartialTranscription(_result.Text, true, _result.Confidence);
    }

    public Task<TranscriptionResult> TranscribeAsync(
        ReadOnlyMemory<byte> pcmAudio,
        CancellationToken ct = default)
        => Task.FromResult(_result);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

// ---------------------------------------------------------------------------
// IBhenguModule (minimal)
// ---------------------------------------------------------------------------

internal sealed class FakeModule : IBhenguModule
{
    public string ModuleName => "Fake";
    public bool IsModelLoaded => true;
    public Task InitAsync(BhenguEngine engine) => Task.CompletedTask;
    public void Dispose() { }
}

// ---------------------------------------------------------------------------
// IModelSource
// ---------------------------------------------------------------------------

/// <summary>
/// Fake model source. Writes a sentinel file on success; throws on demand.
/// The Name property drives host-matching in ModelDownloader.MatchSource.
/// </summary>
internal sealed class FakeModelSource : IModelSource
{
    private readonly bool _shouldThrow;

    public FakeModelSource(string name = "fakehost", bool shouldThrow = false)
    {
        Name = name;
        _shouldThrow = shouldThrow;
    }

    public string Name { get; }
    public int DownloadCallCount { get; private set; }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
        => Task.FromResult(true);

    public Task DownloadAsync(
        string url,
        string localPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        DownloadCallCount++;
        if (_shouldThrow)
            throw new System.Net.Http.HttpRequestException("Simulated source failure");
        // Write a sentinel so callers that check File.Exists pass.
        File.WriteAllText(localPath, "fake-model-content");
        return Task.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// IModelManager
// ---------------------------------------------------------------------------

internal sealed class FakeModelManager : IModelManager
{
    private readonly string _path;

    public FakeModelManager(string path = "fake/path/model.bin") => _path = path;

    public Task<string> GetModelPathAsync(string modelId, CancellationToken ct = default)
        => Task.FromResult(_path);

    public Task<bool> VerifyModelAsync(string modelPath, byte[] expectedChecksum, CancellationToken ct = default)
        => Task.FromResult(true);

    public void Dispose() { }
}

// ---------------------------------------------------------------------------
// IEmbeddingBackend (internal — needs InternalsVisibleTo from Embeddings)
// ---------------------------------------------------------------------------

/// <summary>
/// Fake embedding backend. Returns a fixed float[] of configurable dimension.
/// Used to test TextEmbedder without the native llama.cpp library.
/// </summary>
internal sealed class FakeEmbeddingBackend : IEmbeddingBackend
{
    private readonly float[] _vector;
    private bool _disposed;

    public FakeEmbeddingBackend(int dimension = 4)
    {
        _vector = new float[dimension];
        // Fill with a simple pattern so all dimensions are non-zero.
        for (int i = 0; i < dimension; i++) _vector[i] = 1f / (i + 1);
        // L2-normalise.
        double norm = 0;
        foreach (var x in _vector) norm += (double)x * x;
        norm = Math.Sqrt(norm);
        if (norm > 1e-12) for (int i = 0; i < dimension; i++) _vector[i] /= (float)norm;
    }

    public int Dimension => _vector.Length;

    public float[] Embed(string text)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FakeEmbeddingBackend));
        // Return a copy so callers can't mutate shared state.
        return (float[])_vector.Clone();
    }

    public void Dispose() { _disposed = true; }
}

// ---------------------------------------------------------------------------
// IDeviceContext
// ---------------------------------------------------------------------------

/// <summary>
/// Configurable fake device context for testing context enrichment in AIService.
/// </summary>
internal sealed class FakeDeviceContext : IDeviceContext
{
    public string? ActiveAppId   { get; set; }
    public string? Locale        { get; set; }
    public string? TimeZoneId    { get; set; }
    public DateTimeOffset? LocalTime { get; set; }
    public double? Latitude      { get; set; }
    public double? Longitude     { get; set; }
    public string? LocationHint  { get; set; }
    public float? BatteryLevel   { get; set; }
    public bool? IsCharging      { get; set; }
    public string? NetworkType   { get; set; }
    public DateTimeOffset? LastActiveUtc { get; set; }
}

// ---------------------------------------------------------------------------
// IChatGenerator — captures enriched system prompt for assertion
// ---------------------------------------------------------------------------

/// <summary>
/// Generator that records the system message from every GenerateAsync call
/// so tests can assert on context enrichment without inspecting a black box.
/// </summary>
internal sealed class CapturingChatGenerator : IChatGenerator
{
    private readonly string _reply;
    public List<string?> CapturedSystemMessages { get; } = new();

    public CapturingChatGenerator(string reply = "captured") => _reply = reply;

    public Task<string> GenerateAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var sys = messages.FirstOrDefault(m =>
            string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))?.Content;
        CapturedSystemMessages.Add(sys);
        return Task.FromResult(_reply);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Capture the system message from the stream path too.
        var sys = messages.FirstOrDefault(m =>
            string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))?.Content;
        CapturedSystemMessages.Add(sys);
        await Task.Yield();
        yield return _reply;
    }

    public void Dispose() { }
}

// ---------------------------------------------------------------------------
// IChatGenerator — returns a tool-call response on first call, plain text after
// ---------------------------------------------------------------------------

/// <summary>
/// Generator that returns a <c>&lt;tool_call&gt;</c> block on the first
/// GenerateAsync call and a plain-text answer on subsequent calls. Used to
/// test the agentic loop in AIService.
/// </summary>
internal sealed class AgenticFakeChatGenerator : IChatGenerator
{
    private readonly string _toolName;
    private readonly string _finalAnswer;
    private int _callCount;

    public AgenticFakeChatGenerator(
        string toolName = "tgn.test.ping",
        string finalAnswer = "Done!")
    {
        _toolName = toolName;
        _finalAnswer = finalAnswer;
    }

    public Task<string> GenerateAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _callCount++;
        if (_callCount == 1)
            return Task.FromResult(
                $"<tool_call>{{\"name\":\"{_toolName}\",\"arguments\":{{}}}}</tool_call>");
        return Task.FromResult(_finalAnswer);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return _finalAnswer;
    }

    public void Dispose() { }
}
