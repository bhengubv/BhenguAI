// ButlerOptions.cs
//
// Configuration bag for the long-lived butler service. All fields have safe
// defaults so callers can `new ButlerOptions()` and get a working instance
// (assuming the model identified by <see cref="ModelId"/> is registered with
// the supplied <see cref="Bhengu.AI.Core.IModelLoader"/>).

using System;
using System.Security.Cryptography;
using Bhengu.AI.Inference;
using Bhengu.AI.Tools;

namespace Bhengu.AI.Hosting;

/// <summary>
/// Configuration for <see cref="ButlerService"/> and the loopback transport.
/// </summary>
public sealed class ButlerOptions
{
    /// <summary>
    /// Logical model identifier passed to <see cref="Bhengu.AI.Core.IModelLoader"/>
    /// when <see cref="ModelPath"/> is <c>null</c>. Default <c>"Qwen3-14B-Q4"</c>.
    /// </summary>
    public string ModelId { get; init; } = "Qwen3-14B-Q4";

    /// <summary>
    /// Optional absolute path to a GGUF model file. If set, the service skips
    /// the model-loader registry and uses this file directly. Useful for
    /// developer machines and tests.
    /// </summary>
    public string? ModelPath { get; init; }

    /// <summary>
    /// System prompt prepended to chat conversations that don't already
    /// carry a system message. Applied per-request, not stored in any state.
    /// </summary>
    public string SystemPrompt { get; init; } = "You are B!, a helpful on-device assistant.";

    /// <summary>
    /// Default sampling knobs applied when a caller doesn't pass their own
    /// <see cref="GenerationOptions"/>.
    /// </summary>
    public GenerationOptions? DefaultGenerationOptions { get; init; }

    /// <summary>
    /// Maximum context window in tokens for the loaded model. Default 4096;
    /// raise for longer conversations at the cost of RAM.
    /// </summary>
    public int ContextSize { get; init; } = 4096;

    /// <summary>
    /// CPU threads dedicated to decode. <c>null</c> lets the inference layer
    /// pick a default (usually <c>Environment.ProcessorCount</c>).
    /// </summary>
    public int? ThreadCount { get; init; }

    /// <summary>
    /// Optional bridge for tool calls. If <c>null</c>, <see cref="ButlerService.InvokeToolAsync"/>
    /// returns a failure result.
    /// </summary>
    public IToolBridge? ToolBridge { get; init; }

    /// <summary>
    /// When <c>true</c> (default) <see cref="ButlerService.StartAsync"/> runs
    /// a 1-token generation after loading so the first user request doesn't
    /// pay the cold-start cost.
    /// </summary>
    public bool WarmOnStart { get; init; } = true;

    // ------------------------------------------------------------------
    // HttpLoopbackEndpoint configuration
    // ------------------------------------------------------------------

    /// <summary>
    /// TCP port the loopback HTTP endpoint binds to on <c>127.0.0.1</c>.
    /// <c>0</c> (default) lets the OS pick a free port — read it back via
    /// <see cref="Endpoints.HttpLoopbackEndpoint.BoundPort"/> after start.
    /// </summary>
    public int LoopbackPort { get; init; } = 0;

    /// <summary>
    /// Shared-secret token clients must send in the <c>X-Butler-Token</c>
    /// header. If <c>null</c>, the endpoint generates a random 32-byte
    /// base64 token at startup; read it back via
    /// <see cref="Endpoints.HttpLoopbackEndpoint.Token"/>.
    /// </summary>
    public string? LoopbackToken { get; init; }

    /// <summary>
    /// Generates a cryptographically random 32-byte token, base64-encoded.
    /// Used by <see cref="Endpoints.HttpLoopbackEndpoint"/> when
    /// <see cref="LoopbackToken"/> is <c>null</c>.
    /// </summary>
    public static string GenerateRandomToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
