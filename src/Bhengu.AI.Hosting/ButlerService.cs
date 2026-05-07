// ButlerService.cs
//
// Default IButlerService implementation. Holds a single QwenTextGenerator for
// the lifetime of the host process so the 8 GB model isn't reloaded per call.
//
// Threading model:
//   - StartAsync is idempotent and serialised by SemaphoreSlim. Concurrent
//     callers wait for the single load to finish.
//   - ChatAsync / StreamAsync are safe to call concurrently because
//     QwenTextGenerator allocates a fresh inference context per call.
//   - DisposeAsync cancels in-flight stream calls via the linked CTS held in
//     _shutdownCts.
//
// Observer model:
//   - IButlerObserver is called at key lifecycle and inference events.
//   - Observer exceptions are caught and logged; they never break the caller.
//   - All observer calls are fire-and-forget-with-error-isolation via
//     FireObserverAsync, keeping the hot paths clean.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Bhengu.AI.Core;
using Bhengu.AI.Inference;
using Bhengu.AI.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bhengu.AI.Hosting;

/// <summary>
/// Long-lived butler service. Loads a Qwen GGUF model once and serves all
/// downstream callers from that single handle.
/// </summary>
public sealed class ButlerService : IButlerService
{
    private readonly ButlerOptions _options;
    private readonly IModelLoader? _modelLoader;
    private readonly Func<string, IChatGenerator>? _generatorFactory;
    private readonly ILogger<ButlerService> _logger;

    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();

    private IChatGenerator? _generator;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Constructs the service. Either <paramref name="modelLoader"/> or
    /// <paramref name="generatorFactory"/> must be able to resolve a model
    /// for the given <see cref="ButlerOptions"/>; both may be supplied, in
    /// which case the factory wins.
    /// </summary>
    /// <param name="options">Configuration for the service.</param>
    /// <param name="modelLoader">
    /// Optional. Used to resolve / download the GGUF file when
    /// <see cref="ButlerOptions.ModelPath"/> is <c>null</c>.
    /// </param>
    /// <param name="generatorFactory">
    /// Optional. If supplied, takes precedence over the default
    /// <see cref="QwenTextGenerator"/> path. Receives the resolved model
    /// path and must return a non-null generator.
    /// </param>
    /// <param name="logger">Optional logger. Defaults to <see cref="NullLogger"/>.</param>
    public ButlerService(
        ButlerOptions options,
        IModelLoader? modelLoader = null,
        Func<string, IChatGenerator>? generatorFactory = null,
        ILogger<ButlerService>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _modelLoader = modelLoader;
        _generatorFactory = generatorFactory;
        _logger = logger ?? NullLogger<ButlerService>.Instance;
    }

    /// <inheritdoc />
    public bool IsReady => _started && _generator is not null && !_disposed;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (_started) return;

        await _startGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_started) return;

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
                try
                {
                    await WarmUpAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Warm-up failures are not fatal — log and continue.
                    _logger.LogWarning(ex, "Butler warm-up failed; continuing anyway.");
                }
            }

            _started = true;
            _logger.LogInformation("Butler service ready.");

            await FireObserverAsync(o => o.OnStartedAsync(ct), ct).ConfigureAwait(false);
        }
        finally
        {
            _startGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_disposed) return;

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
            _logger.LogInformation("Butler service stopped.");

            await FireObserverAsync(o => o.OnStoppedAsync(CancellationToken.None),
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _startGate.Release();
        }
    }

    /// <inheritdoc />
    public Task<string> AskAsync(string question, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(question);
        var messages = new List<ChatMessage>
        {
            new("system", _options.SystemPrompt),
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

        var prepared = PrepareMessages(messages);
        var effectiveOptions = options ?? _options.DefaultGenerationOptions;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);

        var correlationId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();
        var response = await generator.GenerateAsync(prepared, effectiveOptions, linked.Token)
            .ConfigureAwait(false);
        sw.Stop();

        await FireObserverAsync(o => o.OnChatCompletedAsync(
            new ButlerChatEvent(correlationId, prepared, response, sw.Elapsed, DateTimeOffset.UtcNow),
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

        var prepared = PrepareMessages(messages);
        var effectiveOptions = options ?? _options.DefaultGenerationOptions;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);

        var correlationId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();
        var tokenCount = 0;
        var firstToken = true;

        await foreach (var piece in generator.StreamAsync(prepared, effectiveOptions, linked.Token)
            .ConfigureAwait(false))
        {
            if (firstToken)
            {
                firstToken = false;
                await FireObserverAsync(o => o.OnStreamStartedAsync(
                    new ButlerStreamEvent(correlationId, prepared, sw.Elapsed, 0, DateTimeOffset.UtcNow),
                    ct), ct).ConfigureAwait(false);
            }

            tokenCount++;
            yield return piece;
        }

        sw.Stop();
        await FireObserverAsync(o => o.OnStreamCompletedAsync(
            new ButlerStreamEvent(correlationId, prepared, sw.Elapsed, tokenCount, DateTimeOffset.UtcNow),
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
                new ButlerToolEvent(Guid.NewGuid(), invocation, failResult,
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
            new ButlerToolEvent(correlationId, invocation, result, sw.Elapsed, DateTimeOffset.UtcNow),
            ct), ct).ConfigureAwait(false);

        return result;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { _shutdownCts.Cancel(); } catch { /* already disposed */ }

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Swallow — DisposeAsync must not throw.
        }

        _shutdownCts.Dispose();
        _startGate.Dispose();
    }

    // ------------------------------------------------------------------
    // Internals
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
                    "Configured ButlerOptions.ModelPath does not exist.",
                    _options.ModelPath);
            return _options.ModelPath!;
        }

        if (_modelLoader is null)
            throw new InvalidOperationException(
                "ButlerService needs either ButlerOptions.ModelPath or an IModelLoader to resolve the model.");

        // Returns null if not yet downloaded.
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
        var warmOptions = new GenerationOptions
        {
            MaxTokens = 1,
            Temperature = 0f,
        };

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);
        _ = await generator.GenerateAsync(warmMessages, warmOptions, linked.Token).ConfigureAwait(false);
    }

    private List<ChatMessage> PrepareMessages(IReadOnlyList<ChatMessage> messages)
    {
        var hasSystem = messages.Any(m =>
            string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));

        var prepared = new List<ChatMessage>(messages.Count + 1);
        if (!hasSystem && !string.IsNullOrEmpty(_options.SystemPrompt))
            prepared.Add(new ChatMessage("system", _options.SystemPrompt));
        prepared.AddRange(messages);
        return prepared;
    }

    /// <summary>
    /// Calls <paramref name="action"/> on the configured observer, catching and
    /// logging any exceptions so they never propagate to the butler caller.
    /// </summary>
    private async ValueTask FireObserverAsync(
        Func<IButlerObserver, ValueTask> action,
        CancellationToken ct)
    {
        if (_options.Observer is null) return;
        try
        {
            await action(_options.Observer).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* respect cancellation silently */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IButlerObserver threw an exception; observer errors are non-fatal.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ButlerService));
    }
}
