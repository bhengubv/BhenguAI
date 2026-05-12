// AIService.cs
//
// Default IAIService implementation. Holds a single QwenTextGenerator for
// the lifetime of the host process so the model isn't reloaded per call.
//
// Threading model:
//   - StartAsync is idempotent and serialised by SemaphoreSlim.
//   - ChatAsync / StreamAsync are safe to call concurrently.
//   - DisposeAsync cancels in-flight stream calls via _shutdownCts.
//
// v2.0 additions:
//   - EnrichSystemPromptAsync — injects device context, RAG snippets, persona
//   - AgenticChatAsync       — loops on tool calls until plain-text response
//   - SubmitFeedbackAsync    — records user feedback signals
//   - Episodic store writes  — exchanges are stored after every ChatAsync

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bhengu.AI.Core;
using Bhengu.AI.Inference;
using Bhengu.AI.Memory;
using Bhengu.AI.Skills;
using Bhengu.AI.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bhengu.AI.Hosting;

/// <summary>
/// Long-lived butler service. Loads a Qwen GGUF model once and serves all
/// downstream callers from that single handle.
/// </summary>
public sealed class AIService : IAIService
{
    private readonly AIOptions _options;
    private readonly IModelLoader? _modelLoader;
    private readonly Func<string, IChatGenerator>? _generatorFactory;
    private readonly ILogger<AIService> _logger;

    private readonly SemaphoreSlim _startGate = new(1, 1);
    private CancellationTokenSource _shutdownCts = new();

    private IChatGenerator? _generator;
    private bool _started;
    private bool _disposed;

    // v2.0 — lazy runtime state
    private PersonaState? _personaCache;
    private RagContextBuilder? _ragBuilder;
    private SkillContextBuilder? _skillContextBuilder;

    // Tool call detection tags (Qwen3 native format).
    private const string ToolCallOpen  = "<tool_call>";
    private const string ToolCallClose = "</tool_call>";

    /// <summary>
    /// Constructs the service. Either <paramref name="modelLoader"/> or
    /// <paramref name="generatorFactory"/> must be able to resolve a model.
    /// </summary>
    public AIService(
        AIOptions options,
        IModelLoader? modelLoader = null,
        Func<string, IChatGenerator>? generatorFactory = null,
        ILogger<AIService>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _modelLoader = modelLoader;
        _generatorFactory = generatorFactory;
        _logger = logger ?? NullLogger<AIService>.Instance;
    }

    /// <inheritdoc />
    public bool IsReady => _started && _generator is not null && !_disposed;

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (_started) return;

        await _startGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_started) return;

            // Apply host-injected native library directory (e.g. Android nativeLibraryDir)
            // before any P/Invoke triggers the DLL resolver callback.
            if (!string.IsNullOrWhiteSpace(_options.NativeLibDir))
                NativeLibraryResolver.OverrideDirectory = _options.NativeLibDir;
            NativeLibraryResolver.EnsureRegistered();

            var modelPath = await ResolveModelPathAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Butler loading model from {ModelPath}", modelPath);

            var generator = _generatorFactory is not null
                ? _generatorFactory(modelPath)
                : new QwenTextGenerator(
                    modelPath,
                    contextSize: (uint)Math.Max(1, _options.ContextSize),
                    threads: _options.ThreadCount);

            if (generator is null)
                throw new InvalidOperationException("Generator factory returned null.");

            _generator = generator;

            if (_options.WarmOnStart)
            {
                try { await WarmUpAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Butler warm-up failed; continuing anyway.");
                }
            }

            _started = true;
            _logger.LogInformation("Butler service ready.");

            await FireObserverAsync(o => o.OnStartedAsync(ct), ct).ConfigureAwait(false);
        }
        finally { _startGate.Release(); }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_disposed) return;

        // Persist persona before teardown.
        await TrySavePersonaAsync().ConfigureAwait(false);

        await _startGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_shutdownCts.IsCancellationRequested)
            {
                try { _shutdownCts.Cancel(); } catch { /* already disposed */ }
            }

            if (_generator is IAsyncDisposable adisp)
                await adisp.DisposeAsync().ConfigureAwait(false);
            else
                _generator?.Dispose();

            _generator = null;
            _started = false;
            _personaCache = null;

            _logger.LogInformation("Butler service stopped.");

            await FireObserverAsync(o => o.OnStoppedAsync(CancellationToken.None),
                CancellationToken.None).ConfigureAwait(false);

            if (!_disposed)
            {
                var old = _shutdownCts;
                _shutdownCts = new CancellationTokenSource();
                try { old.Dispose(); } catch { /* already cancelled/disposed */ }
            }
        }
        finally { _startGate.Release(); }
    }

    // ------------------------------------------------------------------
    // Single-turn inference
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public Task<string> AskAsync(string question, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(question);
        // Pass only the user message — PrepareMessagesAsync will inject the
        // enriched system prompt (persona + device context + RAG).
        var messages = new List<ChatMessage>
        {
            new("user", question),
        };
        return ChatAsync(messages, _options.DefaultGenerationOptions, ct);
    }

    /// <inheritdoc />
    public async Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        await EnsureStartedAsync(ct).ConfigureAwait(false);

        var generator = _generator
            ?? throw new InvalidOperationException("Butler is not ready.");

        // Determine the user query for RAG lookup (last user message).
        var userQuery = messages.LastOrDefault(m =>
            string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content
            ?? string.Empty;

        var prepared = await PrepareMessagesAsync(messages, userQuery, ct)
            .ConfigureAwait(false);
        var effectiveOptions = options ?? _options.DefaultGenerationOptions;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);

        var correlationId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();
        var response = await generator
            .GenerateAsync(prepared, effectiveOptions, linked.Token)
            .ConfigureAwait(false);
        sw.Stop();

        // Store exchange in episodic memory (fire-and-forget with error isolation).
        _ = TryStoreEpisodeAsync(userQuery, response, ct);

        await FireObserverAsync(o => o.OnChatCompletedAsync(
            new AIChatEvent(correlationId, prepared, response, sw.Elapsed, DateTimeOffset.UtcNow),
            ct), ct).ConfigureAwait(false);

        return response;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        await EnsureStartedAsync(ct).ConfigureAwait(false);

        var generator = _generator
            ?? throw new InvalidOperationException("Butler is not ready.");

        var userQuery = messages.LastOrDefault(m =>
            string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content
            ?? string.Empty;

        var prepared = await PrepareMessagesAsync(messages, userQuery, ct)
            .ConfigureAwait(false);
        var effectiveOptions = options ?? _options.DefaultGenerationOptions;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);

        var correlationId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();
        var tokenCount = 0;
        var firstToken = true;
        var sb = new StringBuilder();

        await foreach (var piece in generator.StreamAsync(prepared, effectiveOptions, linked.Token)
            .ConfigureAwait(false))
        {
            if (firstToken)
            {
                firstToken = false;
                await FireObserverAsync(o => o.OnStreamStartedAsync(
                    new AIStreamEvent(correlationId, prepared, sw.Elapsed, 0, DateTimeOffset.UtcNow),
                    ct), ct).ConfigureAwait(false);
            }

            sb.Append(piece);
            tokenCount++;
            yield return piece;
        }

        sw.Stop();

        // Store the full streamed response episodically.
        _ = TryStoreEpisodeAsync(userQuery, sb.ToString(), ct);

        await FireObserverAsync(o => o.OnStreamCompletedAsync(
            new AIStreamEvent(correlationId, prepared, sw.Elapsed, tokenCount, DateTimeOffset.UtcNow),
            ct), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ToolResult> InvokeToolAsync(ToolInvocation invocation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ThrowIfDisposed();

        if (_options.ToolBridge is null)
        {
            var failResult = new ToolResult
            {
                ToolName = invocation.ToolName,
                Success = false,
                Error = "No tool bridge configured.",
            };

            await FireObserverAsync(o => o.OnToolInvokedAsync(
                new AIToolEvent(Guid.NewGuid(), invocation, failResult,
                    TimeSpan.Zero, DateTimeOffset.UtcNow),
                ct), ct).ConfigureAwait(false);

            return failResult;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);

        var correlationId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();
        var result = await _options.ToolBridge.InvokeAsync(invocation, linked.Token).ConfigureAwait(false);
        sw.Stop();

        await FireObserverAsync(o => o.OnToolInvokedAsync(
            new AIToolEvent(correlationId, invocation, result, sw.Elapsed, DateTimeOffset.UtcNow),
            ct), ct).ConfigureAwait(false);

        return result;
    }

    // ------------------------------------------------------------------
    // v2.0 — Agentic loop
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<string> AgenticChatAsync(
        string prompt,
        GenerationOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(prompt);
        await EnsureStartedAsync(ct).ConfigureAwait(false);

        var generator = _generator
            ?? throw new InvalidOperationException("Butler is not ready.");

        var maxIter = Math.Max(1, _options.AgenticMaxIterations);
        var effectiveOptions = options ?? _options.DefaultGenerationOptions;

        // Build conversation history with just the user turn.
        // PrepareMessagesAsync injects the enriched system prompt on every
        // iteration so the model always has fresh persona/device/RAG context.
        var history = new List<ChatMessage>
        {
            new("user", prompt),
        };

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);

        string lastResponse = string.Empty;
        for (int iteration = 0; iteration < maxIter; iteration++)
        {
            var prepared = await PrepareMessagesAsync(history, prompt, linked.Token)
                .ConfigureAwait(false);

            var sw = Stopwatch.StartNew();
            var response = await generator
                .GenerateAsync(prepared, effectiveOptions, linked.Token)
                .ConfigureAwait(false);
            sw.Stop();

            lastResponse = response;

            // Append the assistant turn to history for the next iteration.
            history.Add(new ChatMessage("assistant", response));

            await FireObserverAsync(o => o.OnChatCompletedAsync(
                new AIChatEvent(Guid.NewGuid(), prepared, response, sw.Elapsed, DateTimeOffset.UtcNow),
                ct), ct).ConfigureAwait(false);

            // Try to extract a tool call from the response.
            var invocation = ParseToolCall(response);
            if (invocation is null) break; // No tool call — we're done.

            if (_options.ToolBridge is null)
            {
                // No bridge — append an error result and re-prompt so the model
                // can respond without the tool (graceful degradation).
                history.Add(new ChatMessage("tool",
                    $"{{\"tool\": \"{invocation.ToolName}\", \"error\": \"No tool bridge configured.\"}}"));
                continue;
            }

            // Execute the tool and append the result.
            var toolResult = await InvokeToolAsync(invocation, linked.Token).ConfigureAwait(false);
            var toolContent = toolResult.Success
                ? $"{{\"tool\": \"{toolResult.ToolName}\", \"result\": {JsonSerializer.Serialize(toolResult.Result)}}}"
                : $"{{\"tool\": \"{toolResult.ToolName}\", \"error\": {JsonSerializer.Serialize(toolResult.Error)}}}";

            history.Add(new ChatMessage("tool", toolContent));
            // Loop back to re-prompt with tool result in history.
        }

        // Store the entire agentic exchange as a single episode.
        _ = TryStoreEpisodeAsync(prompt, lastResponse, ct);

        return lastResponse;
    }

    // ------------------------------------------------------------------
    // v2.0 — Feedback
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task SubmitFeedbackAsync(FeedbackSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ThrowIfDisposed();

        if (_options.FeedbackStore is null) return;

        try
        {
            await _options.FeedbackStore.AddAsync(signal, ct).ConfigureAwait(false);

            // Update in-memory persona from the signal.
            var persona = await EnsurePersonaAsync(ct).ConfigureAwait(false);
            if (signal.Polarity == FeedbackPolarity.Positive)
                persona.PositiveSignals++;
            else if (signal.Polarity == FeedbackPolarity.Negative)
                persona.NegativeSignals++;
            persona.TotalInteractions++;

            // Gap 6 — run FeedbackAnalyser and apply persona adaptations.
            var recentSignals = await _options.FeedbackStore
                .GetRecentAsync(20, ct)
                .ConfigureAwait(false);

            var adaptation = new FeedbackAnalyser().Analyse(recentSignals);

            // Verbosity: float delta maps to string state machine.
            if (adaptation.VerbosityDelta < 0f)
                persona.Verbosity = persona.Verbosity switch
                {
                    "detailed" => "balanced",
                    _          => "brief",
                };
            else if (adaptation.VerbosityDelta > 0f)
                persona.Verbosity = persona.Verbosity switch
                {
                    "brief" => "balanced",
                    _       => "detailed",
                };

            // Formality: same pattern (analyser returns 0 currently; wired for future).
            if (adaptation.FormalityDelta < 0f)
                persona.Formality = persona.Formality switch
                {
                    "formal"  => "neutral",
                    _         => "casual",
                };
            else if (adaptation.FormalityDelta > 0f)
                persona.Formality = persona.Formality switch
                {
                    "casual" => "neutral",
                    _        => "formal",
                };

            // Accumulate preferred topic weights.
            foreach (var topic in adaptation.PreferredTopics)
            {
                persona.TopicWeights.TryGetValue(topic, out var existing);
                persona.TopicWeights[topic] = existing + 1f;
            }

            await TrySavePersonaAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store feedback signal; non-fatal.");
        }
    }

    // ------------------------------------------------------------------
    // DisposeAsync
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { _shutdownCts.Cancel(); } catch { /* already disposed */ }

        // Persist persona before teardown.
        await TrySavePersonaAsync().ConfigureAwait(false);

        try { await StopAsync(CancellationToken.None).ConfigureAwait(false); }
        catch { /* swallow */ }

        if (_generator is IAsyncDisposable adisp)
            try { await adisp.DisposeAsync().ConfigureAwait(false); } catch { /* swallow */ }
        else
            try { _generator?.Dispose(); } catch { /* swallow */ }
        _generator = null;

        _shutdownCts.Dispose();
        _startGate.Dispose();
    }

    // ------------------------------------------------------------------
    // Private — startup helpers
    // ------------------------------------------------------------------

    private async Task EnsureStartedAsync(CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_started) return;
        await StartAsync(ct).ConfigureAwait(false);
    }

    private async Task<string> ResolveModelPathAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_options.ModelPath))
        {
            if (!System.IO.File.Exists(_options.ModelPath))
                throw new System.IO.FileNotFoundException(
                    "Configured AIOptions.ModelPath does not exist.",
                    _options.ModelPath);
            return _options.ModelPath!;
        }

        if (_modelLoader is null)
            throw new InvalidOperationException(
                "AIService needs either AIOptions.ModelPath or an IModelLoader.");

        var existing = _modelLoader.GetModelPath(_options.ModelId);
        if (!string.IsNullOrEmpty(existing) && System.IO.File.Exists(existing))
            return existing;

        ct.ThrowIfCancellationRequested();
        _logger.LogInformation("Butler downloading model {ModelId}", _options.ModelId);
        var downloaded = await _modelLoader.DownloadModelAsync(_options.ModelId).ConfigureAwait(false);
        if (string.IsNullOrEmpty(downloaded) || !System.IO.File.Exists(downloaded))
            throw new InvalidOperationException(
                $"Model loader returned an invalid path for '{_options.ModelId}'.");
        return downloaded;
    }

    private async Task WarmUpAsync(CancellationToken ct)
    {
        var generator = _generator;
        if (generator is null) return;

        var warmMessages = new[]
        {
            new ChatMessage("system", _options.SystemPrompt),
            new ChatMessage("user", "."),
        };
        var warmOptions = new GenerationOptions { MaxTokens = 1, Temperature = 0f };

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);
        _ = await generator.GenerateAsync(warmMessages, warmOptions, linked.Token).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Private — v2.0 context enrichment
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds the enriched message list:
    ///   1. Augmented system prompt (persona hints + device context + RAG snippets)
    ///   2. Original conversation messages
    /// </summary>
    private async Task<List<ChatMessage>> PrepareMessagesAsync(
        IReadOnlyList<ChatMessage> messages,
        string userQuery,
        CancellationToken ct)
    {
        var systemContent = await BuildEnrichedSystemPromptAsync(userQuery, ct)
            .ConfigureAwait(false);

        var hasSystem = messages.Any(m =>
            string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));

        var prepared = new List<ChatMessage>(messages.Count + 1);

        if (hasSystem)
        {
            // Caller supplied their own system message — honour it exactly as-is.
            // (Enrichment from options/persona/device/RAG is only applied when the
            // caller has not taken ownership of the system turn.)
            prepared.AddRange(messages);
        }
        else
        {
            // Inject enriched system prompt only when it is non-empty.
            if (!string.IsNullOrWhiteSpace(systemContent))
                prepared.Add(new ChatMessage("system", systemContent));
            prepared.AddRange(messages);
        }

        return prepared;
    }

    private async Task<string> BuildEnrichedSystemPromptAsync(
        string userQuery, CancellationToken ct)
    {
        var sb = new StringBuilder(_options.SystemPrompt);

        // 1. Persona hints.
        try
        {
            var persona = await EnsurePersonaAsync(ct).ConfigureAwait(false);
            var hint = persona.ToSystemPromptHint();
            if (!string.IsNullOrWhiteSpace(hint))
            {
                sb.AppendLine();
                sb.Append(hint);
            }
        }
        catch { /* persona load failure is non-fatal */ }

        // 1b. Affect state.
        if (_options.AffectStore is not null)
        {
            try
            {
                var affect = await _options.AffectStore.LoadAsync(_options.PersonaUserId, ct).ConfigureAwait(false);
                var hint = affect.ToSystemPromptHint();
                if (!string.IsNullOrWhiteSpace(hint))
                {
                    sb.AppendLine();
                    sb.Append(hint);
                }
            }
            catch { /* affect load failure is non-fatal */ }
        }

        // 2. Device context.
        var ctx = _options.DeviceContext;
        if (ctx is not null && ctx is not NullDeviceContext)
        {
            var ctxLines = new List<string>();
            if (ctx.LocalTime.HasValue)
                ctxLines.Add($"Local time: {ctx.LocalTime.Value:yyyy-MM-dd HH:mm} ({ctx.TimeZoneId ?? "UTC"})");
            if (!string.IsNullOrWhiteSpace(ctx.LocationHint))
                ctxLines.Add($"Location: {ctx.LocationHint}");
            if (ctx.BatteryLevel.HasValue)
            {
                var pct = (int)(ctx.BatteryLevel.Value * 100);
                var charging = ctx.IsCharging == true ? " (charging)" : string.Empty;
                ctxLines.Add($"Battery: {pct}%{charging}");
            }
            if (!string.IsNullOrWhiteSpace(ctx.NetworkType))
                ctxLines.Add($"Network: {ctx.NetworkType}");
            if (!string.IsNullOrWhiteSpace(ctx.ActiveAppId))
                ctxLines.Add($"Active app: {ctx.ActiveAppId}");

            if (ctxLines.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[Device context]");
                foreach (var line in ctxLines)
                    sb.AppendLine(line);
            }
        }

        // 3. RAG context (relevant past exchanges).
        if (_options.EpisodicMemory is not null && _options.RagTopK > 0 &&
            !string.IsNullOrWhiteSpace(userQuery))
        {
            try
            {
                var builder = EnsureRagBuilder();
                var ragBlock = await builder.BuildContextAsync(userQuery, ct)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(ragBlock))
                {
                    sb.AppendLine();
                    sb.Append(ragBlock);
                }
            }
            catch { /* RAG failure is non-fatal */ }
        }

        // 4. Skill context (relevant capability definitions).
        if (_options.SkillStore is not null && !string.IsNullOrWhiteSpace(userQuery))
        {
            try
            {
                var skillBuilder = EnsureSkillContextBuilder();
                var skillBlock = await skillBuilder.BuildContextAsync(userQuery, ct)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(skillBlock))
                {
                    sb.AppendLine();
                    sb.Append(skillBlock);
                }
            }
            catch { /* skill context failure is non-fatal */ }
        }

        return sb.ToString();
    }

    private SkillContextBuilder EnsureSkillContextBuilder()
    {
        if (_skillContextBuilder is not null) return _skillContextBuilder;
        _skillContextBuilder = new SkillContextBuilder(_options.SkillStore!, _options.SkillTopK);
        return _skillContextBuilder;
    }

    private RagContextBuilder EnsureRagBuilder()
    {
        if (_ragBuilder is not null) return _ragBuilder;
        _ragBuilder = _options.RagBuilder
            ?? new RagContextBuilder(
                _options.EpisodicMemory!,
                embedder: null,          // recency-only until embedder is wired up
                topK: _options.RagTopK);
        return _ragBuilder;
    }

    // ------------------------------------------------------------------
    // Private — persona helpers
    // ------------------------------------------------------------------

    private async Task<PersonaState> EnsurePersonaAsync(CancellationToken ct)
    {
        if (_personaCache is not null) return _personaCache;
        if (_options.PersonaStore is null)
        {
            _personaCache = new PersonaState { UserId = _options.PersonaUserId };
            return _personaCache;
        }

        _personaCache = await _options.PersonaStore
            .LoadAsync(_options.PersonaUserId, ct)
            .ConfigureAwait(false);
        return _personaCache;
    }

    private async Task TrySavePersonaAsync()
    {
        if (_personaCache is null || _options.PersonaStore is null) return;
        try
        {
            await _options.PersonaStore
                .SaveAsync(_personaCache, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist persona state; non-fatal.");
        }
    }

    // ------------------------------------------------------------------
    // Private — episodic memory
    // ------------------------------------------------------------------

    private async Task TryStoreEpisodeAsync(
        string userText, string assistantText, CancellationToken ct)
    {
        if (_options.EpisodicMemory is null) return;
        if (string.IsNullOrWhiteSpace(userText)) return;

        try
        {
            var entry = new EpisodicMemoryEntry
            {
                UserText      = userText,
                AssistantText = assistantText,
                AppContext     = _options.DeviceContext?.ActiveAppId,
                // Embedding is left null here; a background service can
                // back-fill embeddings when the embedding model is available.
                Embedding = null,
            };
            await _options.EpisodicMemory.AddAsync(entry, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Episodic store write failed; non-fatal.");
        }
    }

    // ------------------------------------------------------------------
    // Private — tool call parsing
    // ------------------------------------------------------------------

    /// <summary>
    /// Attempts to parse a tool call from Qwen3's native
    /// <c>&lt;tool_call&gt;...&lt;/tool_call&gt;</c> format.
    /// Returns <c>null</c> when no tool call is present.
    /// </summary>
    internal static ToolInvocation? ParseToolCall(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return null;

        var start = response.IndexOf(ToolCallOpen, StringComparison.Ordinal);
        if (start < 0) return null;

        var contentStart = start + ToolCallOpen.Length;
        var end = response.IndexOf(ToolCallClose, contentStart, StringComparison.Ordinal);
        if (end < 0) return null;

        var json = response[contentStart..end].Trim();
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Support both {"name":...} and {"tool_name":...} spellings.
            var toolName = root.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString()
                : root.TryGetProperty("tool_name", out var tnProp)
                    ? tnProp.GetString()
                    : null;

            if (string.IsNullOrWhiteSpace(toolName)) return null;

            var args = new Dictionary<string, object?>();
            if (root.TryGetProperty("arguments", out var argsProp) &&
                argsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in argsProp.EnumerateObject())
                    args[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()
                        : (object?)prop.Value.GetRawText();
            }

            return new ToolInvocation { ToolName = toolName!, Arguments = args };
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Private — observer
    // ------------------------------------------------------------------

    private async ValueTask FireObserverAsync(
        Func<IAIObserver, ValueTask> action, CancellationToken ct)
    {
        if (_options.Observer is null) return;
        try { await action(_options.Observer).ConfigureAwait(false); }
        catch (OperationCanceledException) { /* respect cancellation silently */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IAIObserver threw; observer errors are non-fatal.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AIService));
    }
}
