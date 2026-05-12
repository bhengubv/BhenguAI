// FallbackAIService.cs
//
// Transparent IAIService wrapper that attempts local inference first and
// falls back to the ButlerAPI cloud endpoint when the device cannot run the
// model locally (no model cached, insufficient RAM, or local start failure).
//
// Usage:
//   var fallback = new FallbackAIService(
//       local:  new AIService(localOptions),
//       cloud:  new AIApiClient(cloudUri, token),
//       ramThresholdBytes: options.CloudFallbackRamThresholdBytes);
//
//   await fallback.StartAsync();   // tries local; silently falls back to cloud

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Circle.AI.Inference;
using Circle.AI.Memory;
using Circle.AI.Tools;
using Microsoft.Extensions.Logging;

namespace Circle.AI.Hosting;

/// <summary>
/// Wraps a local <see cref="IAIService"/> with a cloud
/// <see cref="AIApiClient"/> fallback. Local inference is preferred;
/// cloud is used transparently when local is unavailable.
/// </summary>
public sealed class FallbackAIService : IAIService
{
    // ------------------------------------------------------------------
    // Fields
    // ------------------------------------------------------------------

    private readonly IAIService         _local;
    private readonly AIApiClient        _cloud;
    private readonly long                   _ramThresholdBytes;
    private readonly ILogger<FallbackAIService>? _logger;
    private          IAIService         _active = null!;
    private          bool                   _disposed;

    // ------------------------------------------------------------------
    // IAIService.IsReady
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public bool IsReady => _active?.IsReady ?? false;

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    /// <param name="local">Local <see cref="AIService"/> (on-device inference).</param>
    /// <param name="cloud"><see cref="AIApiClient"/> pointed at ButlerAPI.</param>
    /// <param name="ramThresholdBytes">
    /// Minimum available RAM for local inference. Below this, cloud is used
    /// even when a model is cached. Default 2 GB.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public FallbackAIService(
        IAIService                  local,
        AIApiClient                 cloud,
        long                            ramThresholdBytes = 2L * 1024 * 1024 * 1024,
        ILogger<FallbackAIService>? logger = null)
    {
        _local             = local  ?? throw new ArgumentNullException(nameof(local));
        _cloud             = cloud  ?? throw new ArgumentNullException(nameof(cloud));
        _ramThresholdBytes = ramThresholdBytes;
        _logger            = logger;
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    /// <inheritdoc />
    /// <remarks>
    /// Tries to start local inference. If the device has insufficient RAM
    /// or local start throws, falls back to the cloud endpoint silently.
    /// </remarks>
    public async Task StartAsync(CancellationToken ct = default)
    {
        var availableRam = GetAvailableRamBytes();

        if (availableRam >= _ramThresholdBytes)
        {
            try
            {
                await _local.StartAsync(ct).ConfigureAwait(false);
                _active = _local;
                _logger?.LogInformation(
                    "B! running locally ({RamMb} MB available).",
                    availableRam / (1024 * 1024));
                return;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex,
                    "Local B! start failed — falling back to cloud.");
            }
        }
        else
        {
            _logger?.LogInformation(
                "Available RAM {RamMb} MB below threshold {ThresholdMb} MB — using cloud.",
                availableRam       / (1024 * 1024),
                _ramThresholdBytes / (1024 * 1024));
        }

        await _cloud.StartAsync(ct).ConfigureAwait(false);
        _active = _cloud;
        _logger?.LogInformation("B! running via ButlerAPI cloud fallback.");
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_active is not null)
            await _active.StopAsync(ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Inference — all delegate to the active backend
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public Task<string> AskAsync(string question, CancellationToken ct = default)
        => Active.AskAsync(question, ct);

    /// <inheritdoc />
    public Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        CancellationToken ct = default)
        => Active.ChatAsync(messages, options, ct);

    /// <inheritdoc />
    public IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        CancellationToken ct = default)
        => Active.StreamAsync(messages, options, ct);

    /// <inheritdoc />
    public Task<string> AgenticChatAsync(
        string prompt,
        GenerationOptions? options = null,
        CancellationToken ct = default)
        => Active.AgenticChatAsync(prompt, options, ct);

    /// <inheritdoc />
    public Task<ToolResult> InvokeToolAsync(
        ToolInvocation invocation,
        CancellationToken ct = default)
        => Active.InvokeToolAsync(invocation, ct);

    /// <inheritdoc />
    public Task SubmitFeedbackAsync(FeedbackSignal signal, CancellationToken ct = default)
        => Active.SubmitFeedbackAsync(signal, ct);

    // ------------------------------------------------------------------
    // IAsyncDisposable
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _local.DisposeAsync().ConfigureAwait(false);
        await _cloud.DisposeAsync().ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private IAIService Active =>
        _active ?? throw new InvalidOperationException(
            "FallbackAIService has not been started. Call StartAsync first.");

    private static long GetAvailableRamBytes()
    {
        try { return (long)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes; }
        catch { return 0; }
    }
}
