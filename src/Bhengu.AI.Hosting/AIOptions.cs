// AIOptions.cs
//
// Configuration bag for the long-lived butler service. All fields have safe
// defaults so callers can `new AIOptions()` and get a working instance
// (assuming the model identified by <see cref="ModelId"/> is registered with
// the supplied <see cref="Bhengu.AI.Core.IModelLoader"/>).
//
// v2.0 additions:
//   - DeviceContext  — sensorium (GPS, battery, active app, locale, …)
//   - EpisodicMemory — episodic store + optional RAG context builder
//   - PersonaStore   — per-user evolving persona (tone, topics, verbosity)
//   - FeedbackStore  — user feedback signals for on-device adaptation
//   - AgenticMaxIterations — caps the tool-call→re-prompt loop

using System;
using System.Security.Cryptography;
using Bhengu.AI.Core;
using Bhengu.AI.Inference;
using Bhengu.AI.Memory;
using Bhengu.AI.Skills;
using Bhengu.AI.Tools;

namespace Bhengu.AI.Hosting;

/// <summary>
/// Configuration for <see cref="AIService"/> and the loopback transport.
/// </summary>
public sealed class AIOptions
{
    // ------------------------------------------------------------------
    // Model
    // ------------------------------------------------------------------

    /// <summary>
    /// Logical model identifier passed to <see cref="Bhengu.AI.Core.IModelLoader"/>
    /// when <see cref="ModelPath"/> is <c>null</c>. Default <c>"Qwen3.6-35B-A3B-Q3"</c>.
    /// </summary>
    public string ModelId { get; init; } = "Qwen3.6-35B-A3B-Q3";

    /// <summary>
    /// Optional absolute path to a GGUF model file. If set, the service skips
    /// the model-loader registry and uses this file directly. Useful for
    /// developer machines and tests.
    /// </summary>
    public string? ModelPath { get; init; }

    // ------------------------------------------------------------------
    // Inference
    // ------------------------------------------------------------------

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
    /// When <c>true</c> (default) <see cref="AIService.StartAsync"/> runs
    /// a 1-token generation after loading so the first user request doesn't
    /// pay the cold-start cost.
    /// </summary>
    public bool WarmOnStart { get; init; } = true;

    // ------------------------------------------------------------------
    // Tools
    // ------------------------------------------------------------------

    /// <summary>
    /// Optional bridge for tool calls. If <c>null</c>, <see cref="AIService.InvokeToolAsync"/>
    /// returns a failure result.
    /// </summary>
    public IToolBridge? ToolBridge { get; init; }

    // ------------------------------------------------------------------
    // Observers
    // ------------------------------------------------------------------

    /// <summary>
    /// Optional observer that receives lifecycle and inference events.
    /// Use this to plug in analytics, usage tracking, or platform-specific
    /// economics (e.g. Qi/Karma accumulation) without modifying BhenguAI.
    /// <c>null</c> (default) disables all observer callbacks.
    /// </summary>
    public IAIObserver? Observer { get; init; }

    // ------------------------------------------------------------------
    // v2.0 — Sensorium
    // ------------------------------------------------------------------

    /// <summary>
    /// Platform-provided device context snapshot (GPS, battery, active app,
    /// locale, …). Injected into the system prompt so B! can reason about
    /// the user's real-world state.
    /// <c>null</c> / <see cref="NullDeviceContext"/> → no context injected.
    /// </summary>
    public IDeviceContext? DeviceContext { get; init; }

    // ------------------------------------------------------------------
    // v2.0 — Memory / RAG
    // ------------------------------------------------------------------

    /// <summary>
    /// Episodic memory store. When set, B! stores every exchange and
    /// retrieves relevant past exchanges to inject into the system prompt
    /// before each inference call (retrieval-augmented generation).
    /// </summary>
    public IEpisodicMemoryStore? EpisodicMemory { get; init; }

    /// <summary>
    /// Pre-configured <see cref="RagContextBuilder"/>. When <c>null</c> but
    /// <see cref="EpisodicMemory"/> is set, a recency-only builder is created
    /// automatically (no embedding lookup).
    /// </summary>
    public RagContextBuilder? RagBuilder { get; init; }

    /// <summary>
    /// Number of relevant past episodes to inject per inference call.
    /// Default 5. Set to 0 to disable RAG even when
    /// <see cref="EpisodicMemory"/> is configured.
    /// </summary>
    public int RagTopK { get; init; } = 5;

    // ------------------------------------------------------------------
    // v2.0 — Persona evolution
    // ------------------------------------------------------------------

    /// <summary>
    /// Per-user persona store. When set, <see cref="AIService"/> loads
    /// the persona for <see cref="PersonaUserId"/> on first use and injects
    /// its style hints into the system prompt.
    /// </summary>
    public IPersonaStore? PersonaStore { get; init; }

    /// <summary>
    /// User identifier used to load/save the persona. Defaults to
    /// <c>"default"</c> (single-user device).
    /// </summary>
    public string PersonaUserId { get; init; } = "default";

    // ------------------------------------------------------------------
    // v2.0 — Feedback signals
    // ------------------------------------------------------------------

    /// <summary>
    /// Feedback store for user reactions. When set, callers may submit
    /// <see cref="FeedbackSignal"/> records via the forthcoming
    /// <c>SubmitFeedbackAsync</c> API. Accumulated signals feed persona
    /// evolution and, eventually, on-device LoRA fine-tuning.
    /// </summary>
    public IFeedbackStore? FeedbackStore { get; init; }

    // ------------------------------------------------------------------
    // v2.0 — Agentic loop
    // ------------------------------------------------------------------

    /// <summary>
    /// Maximum tool-call / re-prompt iterations in an agentic run.
    /// Default 5. Set to 0 or 1 to disable agentic looping (single-turn
    /// only). Only respected by <see cref="AIService.AgenticChatAsync"/>.
    /// </summary>
    public int AgenticMaxIterations { get; init; } = 5;

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

    // ------------------------------------------------------------------
    // v2.1 — Native runtime
    // ------------------------------------------------------------------

    /// <summary>
    /// Override the directory searched for llama.cpp / llava native binaries.
    /// When <c>null</c> (default), <see cref="Bhengu.AI.Inference.NativeLibraryResolver"/>
    /// uses the standard <c>runtimes/{RID}/native/</c> layout relative to the
    /// assembly, or the Android <c>nativeLibraryDir</c> on mobile.
    /// Set this to <c>Android.App.Application.Context.ApplicationInfo.NativeLibraryDir</c>
    /// on Android hosts that don't use the MAUI adapter.
    /// </summary>
    public string? NativeLibDir { get; init; }

    // ------------------------------------------------------------------
    // v2.1 — Model management
    // ------------------------------------------------------------------

    /// <summary>
    /// Directory where downloaded GGUF model files are stored.
    /// Defaults to <c>{AppContext.BaseDirectory}/models</c> on desktop
    /// and the app's documents folder on mobile (set by the host).
    /// </summary>
    public string ModelStorageDir { get; init; } =
        Path.Combine(AppContext.BaseDirectory, "models");

    /// <summary>
    /// When <c>true</c>, the model download service only downloads over
    /// Wi-Fi / Ethernet. Defaults to <c>true</c> to protect mobile data.
    /// </summary>
    public bool WifiOnlyModelDownload { get; init; } = true;

    // ------------------------------------------------------------------
    // v2.1 — Cloud fallback
    // ------------------------------------------------------------------

    /// <summary>
    /// When <c>true</c>, requests are routed to <see cref="CloudFallbackEndpoint"/>
    /// when no local model is available or device RAM is below
    /// <see cref="CloudFallbackRamThresholdBytes"/>. Default <c>false</c>.
    /// </summary>
    public bool CloudFallbackEnabled { get; init; } = false;

    /// <summary>
    /// Base URI of the ButlerAPI cloud endpoint (e.g. <c>https://butler.thegeeknetwork.co.za</c>).
    /// Required when <see cref="CloudFallbackEnabled"/> is <c>true</c>.
    /// </summary>
    public Uri? CloudFallbackEndpoint { get; init; }

    /// <summary>
    /// Bearer token for the ButlerAPI cloud endpoint.
    /// </summary>
    public string? CloudFallbackToken { get; init; }

    /// <summary>
    /// Minimum RAM in bytes required for local inference. Below this threshold
    /// the cloud fallback is used even when a model is cached.
    /// Default 2 GB.
    /// </summary>
    public long CloudFallbackRamThresholdBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    // ------------------------------------------------------------------
    // v2.1 — Thermal management
    // ------------------------------------------------------------------

    /// <summary>
    /// When <c>true</c> (default), <see cref="AIService"/> will pause
    /// inference when the device thermal state reaches Serious or Critical.
    /// Requires a <see cref="IThermalThrottleService"/> registered in DI
    /// (or injected via <see cref="ThermalService"/>).
    /// </summary>
    public bool ThermalPauseEnabled { get; init; } = true;

    /// <summary>
    /// Optional thermal throttle service. When <c>null</c> and
    /// <see cref="ThermalPauseEnabled"/> is <c>true</c>, thermal pausing
    /// is silently disabled (no service = no monitoring).
    /// </summary>
    public IThermalThrottleService? ThermalService { get; init; }

    // ------------------------------------------------------------------
    // v3.0 — Voice pipeline
    // ------------------------------------------------------------------

    /// <summary>
    /// Voice pipeline configuration. When <c>null</c> (default), the voice
    /// pipeline is disabled and B! operates in text-only mode.
    /// Set to a <see cref="VoiceOptions"/> instance to enable wake-word
    /// detection, speech-to-text, and TTS response playback.
    /// </summary>
    public VoiceOptions? Voice { get; init; }

    // ------------------------------------------------------------------
    // v3.0 — Scheduled tasks
    // ------------------------------------------------------------------

    /// <summary>
    /// Optional scheduled task store. When set, <see cref="ScheduledAIService"/>
    /// can be used to run recurring B! prompts on a cron schedule.
    /// </summary>
    public IScheduledTaskStore? ScheduledTaskStore { get; init; }

    // ------------------------------------------------------------------
    // v3.0 — Skills
    // ------------------------------------------------------------------

    /// <summary>
    /// Optional skill store. When set, <see cref="SkillContextBuilder"/>
    /// selects the most relevant skills for each user query and injects
    /// them into the system prompt so B! knows which capabilities to apply.
    /// </summary>
    public ISkillStore? SkillStore { get; init; }

    /// <summary>
    /// Maximum number of skills injected per inference call. Default 5.
    /// Only used when <see cref="SkillStore"/> is non-null.
    /// </summary>
    public int SkillTopK { get; init; } = 5;

    // ------------------------------------------------------------------
    // v3.0 — Affect model
    // ------------------------------------------------------------------

    /// <summary>
    /// Affect state store. When set, B!'s emotional state is persisted and
    /// injected into the system prompt to shape response tone and initiative.
    /// </summary>
    public IAffectStore? AffectStore { get; init; }

    // ------------------------------------------------------------------
    // v3.0 — Goal tracking
    // ------------------------------------------------------------------

    /// <summary>
    /// Goal store. When set, B! tracks user goals and proactively helps
    /// with them via <see cref="IProactiveReasoningService"/>.
    /// </summary>
    public IGoalStore? GoalStore { get; init; }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

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
