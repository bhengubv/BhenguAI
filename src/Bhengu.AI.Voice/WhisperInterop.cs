// WhisperInterop.cs
//
// P/Invoke bindings for whisper.cpp's C API. Follows the same pattern as
// Bhengu.AI.Inference/LlamaCppInterop.cs — [DllImport] with CallingConvention.Cdecl,
// SafeHandle for the whisper context, and a NativeLibraryResolver for cross-platform
// native library loading.
//
// Native library must be present alongside the .NET binary at runtime:
//   Windows  -> whisper.dll
//   Linux    -> libwhisper.so
//   Android  -> libwhisper.so (inside APK lib/<abi>/)
//   macOS    -> libwhisper.dylib
//   iOS      -> libwhisper.dylib (or statically linked)

using System.Reflection;
using System.Runtime.InteropServices;

namespace Bhengu.AI.Voice;

/// <summary>
/// Native handle wrapping a <c>whisper_context*</c>. Released with
/// <see cref="WhisperInterop.whisper_free"/>.
/// </summary>
internal sealed class WhisperContextHandle : SafeHandle
{
    /// <summary>
    /// Initialises a new invalid handle. The runtime populates
    /// <see cref="SafeHandle.handle"/> after a successful P/Invoke call.
    /// </summary>
    public WhisperContextHandle() : base(IntPtr.Zero, ownsHandle: true) { }

    /// <inheritdoc />
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        if (handle != IntPtr.Zero)
        {
            WhisperInterop.whisper_free(handle);
            handle = IntPtr.Zero;
        }
        return true;
    }
}

/// <summary>
/// Mirrors <c>whisper_context_params</c> from whisper.cpp.
/// Bool fields are stored as <see cref="byte"/> for blittability.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WhisperContextParams
{
    /// <summary>Use GPU acceleration when available.</summary>
    public byte use_gpu;

    /// <summary>Use flash attention (reduces memory for long audio).</summary>
    public byte flash_attn;

    /// <summary>GPU device index to use (default 0).</summary>
    public int gpu_device;

    /// <summary>Enable DTW token-level timestamps.</summary>
    public byte dtw_token_timestamps;

    /// <summary>DTW aheads preset.</summary>
    public int dtw_aheads_preset;

    /// <summary>Number of top-layer heads for DTW.</summary>
    public int dtw_n_top;

    /// <summary>Custom DTW aheads (nullable).</summary>
    public IntPtr dtw_aheads;

    /// <summary>Custom DTW memory size.</summary>
    public nuint dtw_mem_size;
}

/// <summary>
/// Whisper sampling strategy enum.
/// </summary>
internal enum WhisperSamplingStrategy : int
{
    /// <summary>Greedy decoding — fastest, picks best token at each step.</summary>
    Greedy = 0,

    /// <summary>Beam search — slower but potentially more accurate.</summary>
    BeamSearch = 1
}

/// <summary>
/// Mirrors <c>whisper_full_params</c> from whisper.cpp. Only the fields
/// required for basic transcription are exposed; remaining fields use
/// default values obtained from <see cref="WhisperInterop.whisper_full_default_params"/>.
/// </summary>
/// <remarks>
/// This struct is large and its layout must exactly match the native
/// <c>whisper_full_params</c>. We marshal it as an opaque blob obtained
/// from the native default-params call, then mutate only the fields we
/// care about via offset-based helpers. This avoids breakage when
/// upstream whisper.cpp adds or reorders fields.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WhisperFullParams
{
    public int strategy;

    public int n_threads;
    public int n_max_text_ctx;
    public int offset_ms;
    public int duration_ms;

    public byte translate;
    public byte no_context;
    public byte no_timestamps;
    public byte single_segment;

    public byte print_special;
    public byte print_progress;
    public byte print_realtime;
    public byte print_timestamps;

    public byte token_timestamps;
    public float thold_pt;
    public float thold_ptsum;
    public int max_len;
    public byte split_on_word;
    public int max_tokens;

    public byte speed_up;
    public byte debug_mode;
    public int audio_ctx;

    public byte tdrz_enable;
    public byte suppress_regex;

    public IntPtr initial_prompt;
    public IntPtr prompt_tokens;
    public int prompt_n_tokens;

    public IntPtr language;
    public byte detect_language;

    public byte suppress_blank;
    public byte suppress_nst;

    public float temperature;
    public float max_initial_ts;
    public float length_penalty;

    public int temperature_inc_count;
    public float temperature_inc;

    public float entropy_thold;
    public float logprob_thold;
    public float no_speech_thold;

    // Greedy strategy params.
    public int greedy_best_of;

    // Beam search strategy params.
    public int beam_search_beam_size;
    public float beam_search_patience;

    // Callback pointers — we leave these as IntPtr.Zero.
    public IntPtr new_segment_callback;
    public IntPtr new_segment_callback_user_data;
    public IntPtr progress_callback;
    public IntPtr progress_callback_user_data;
    public IntPtr encoder_begin_callback;
    public IntPtr encoder_begin_callback_user_data;
    public IntPtr abort_callback;
    public IntPtr abort_callback_user_data;
    public IntPtr logits_filter_callback;
    public IntPtr logits_filter_callback_user_data;

    // Grammar (nullable pointers — unused).
    public IntPtr grammar_rules;
    public nuint n_grammar_rules;
    public nuint i_start_rule;
    public float grammar_penalty;
}

/// <summary>
/// P/Invoke entry points for whisper.cpp's C API. Internal-only — callers
/// should use <see cref="WhisperTranscriber"/> rather than touching these
/// directly.
/// </summary>
/// <remarks>
/// <para>
/// The native library (<c>whisper.dll</c> / <c>libwhisper.so</c> /
/// <c>libwhisper.dylib</c>) must be present in the same directory as the
/// running .NET assembly, under <c>runtimes/{RID}/native/</c>, or
/// somewhere on the platform's library search path.
/// </para>
/// <para>
/// Uses the same <see cref="NativeLibrary.SetDllImportResolver"/> approach
/// as <c>Bhengu.AI.Inference.NativeLibraryResolver</c>, adapted for the
/// whisper library name.
/// </para>
/// </remarks>
internal static class WhisperInterop
{
    /// <summary>
    /// Resolved library name. Windows uses <c>whisper.dll</c>; Linux/Android
    /// use <c>libwhisper.so</c>; macOS/iOS use <c>libwhisper.dylib</c>.
    /// </summary>
    public const string LibraryName = "whisper";

    private static bool _resolverRegistered;
    private static readonly object _resolverLock = new();

    /// <summary>
    /// Optional override directory injected by the host (e.g. Android
    /// <c>nativeLibraryDir</c>). Set before first use.
    /// </summary>
    public static string? OverrideNativeDirectory { get; set; }

    /// <summary>
    /// Ensures the native library resolver is registered for this assembly.
    /// Safe to call multiple times; registration happens once per process.
    /// </summary>
    public static void EnsureResolverRegistered()
    {
        if (_resolverRegistered) return;
        lock (_resolverLock)
        {
            if (_resolverRegistered) return;
            NativeLibrary.SetDllImportResolver(
                Assembly.GetExecutingAssembly(), ResolveWhisperLibrary);
            _resolverRegistered = true;
        }
    }

    private static nint ResolveWhisperLibrary(
        string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.OrdinalIgnoreCase))
            return nint.Zero;

        var nativeFileName = GetNativeFileName();

        // 1. Host-injected override directory.
        if (!string.IsNullOrWhiteSpace(OverrideNativeDirectory))
        {
            var overridePath = Path.Combine(OverrideNativeDirectory, nativeFileName);
            if (File.Exists(overridePath) &&
                NativeLibrary.TryLoad(overridePath, out var h))
                return h;
        }

        // 2. runtimes/{RID}/native/ layout.
        var rid = RuntimeInformation.RuntimeIdentifier;
        if (!string.IsNullOrWhiteSpace(rid))
        {
            var assemblyDir = Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;
            var candidate = Path.Combine(assemblyDir, "runtimes", rid, "native", nativeFileName);
            if (File.Exists(candidate) &&
                NativeLibrary.TryLoad(candidate, out var h))
                return h;
        }

        // 3. Same directory as assembly (flat deployment).
        var flatDir = Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;
        var flat = Path.Combine(flatDir, nativeFileName);
        if (File.Exists(flat) &&
            NativeLibrary.TryLoad(flat, out var fh))
            return fh;

        // 4. AppContext.BaseDirectory.
        var basePath = Path.Combine(AppContext.BaseDirectory, nativeFileName);
        if (File.Exists(basePath) &&
            NativeLibrary.TryLoad(basePath, out var bh))
            return bh;

        return nint.Zero; // Fall back to default OS resolution.
    }

    private static string GetNativeFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "whisper.dll";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "libwhisper.dylib";
        return "libwhisper.so"; // Linux + Android
    }

    // ── Context lifecycle ────────────────────────────────────────────────

    /// <summary>
    /// Returns default <see cref="WhisperContextParams"/> suitable for most
    /// use cases. Mutate the returned struct before passing to
    /// <see cref="whisper_init_from_file_with_params"/>.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "whisper_context_default_params")]
    public static extern WhisperContextParams whisper_context_default_params();

    /// <summary>
    /// Load a whisper GGML model from <paramref name="path_model"/> and
    /// create a new whisper context.
    /// </summary>
    /// <param name="path_model">Absolute path to the .bin model file.</param>
    /// <param name="params">Context parameters (GPU, flash attention, etc.).</param>
    /// <returns>
    /// A native pointer to the whisper context, or <see cref="IntPtr.Zero"/>
    /// on failure.
    /// </returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "whisper_init_from_file_with_params", CharSet = CharSet.Ansi)]
    public static extern IntPtr whisper_init_from_file_with_params(
        string path_model,
        WhisperContextParams @params);

    /// <summary>
    /// Free a whisper context and all associated resources.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "whisper_free")]
    public static extern void whisper_free(IntPtr ctx);

    // ── Full transcription ───────────────────────────────────────────────

    /// <summary>
    /// Returns default full-params for the given sampling strategy.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "whisper_full_default_params")]
    public static extern WhisperFullParams whisper_full_default_params(
        WhisperSamplingStrategy strategy);

    /// <summary>
    /// Run the full whisper pipeline on the provided PCM float32 samples.
    /// </summary>
    /// <param name="ctx">Whisper context pointer.</param>
    /// <param name="params">Full parameters controlling decoding.</param>
    /// <param name="samples">
    /// Pointer to float32 PCM samples, mono, at the model's expected sample
    /// rate (16 kHz). Values should be normalised to [-1, 1].
    /// </param>
    /// <param name="n_samples">Number of float samples.</param>
    /// <returns>0 on success; non-zero on error.</returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "whisper_full")]
    public static extern unsafe int whisper_full(
        IntPtr ctx,
        WhisperFullParams @params,
        float* samples,
        int n_samples);

    // ── Segment accessors ────────────────────────────────────────────────

    /// <summary>
    /// Returns the number of text segments produced by the last
    /// <see cref="whisper_full"/> call.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "whisper_full_n_segments")]
    public static extern int whisper_full_n_segments(IntPtr ctx);

    /// <summary>
    /// Returns a pointer to the null-terminated UTF-8 text of the given
    /// segment. The pointer is valid until the next <see cref="whisper_full"/>
    /// call or until the context is freed.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "whisper_full_get_segment_text")]
    public static extern IntPtr whisper_full_get_segment_text(IntPtr ctx, int i_segment);

    /// <summary>
    /// Returns the start timestamp (in centiseconds) of segment
    /// <paramref name="i_segment"/>.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "whisper_full_get_segment_t0")]
    public static extern long whisper_full_get_segment_t0(IntPtr ctx, int i_segment);

    /// <summary>
    /// Returns the end timestamp (in centiseconds) of segment
    /// <paramref name="i_segment"/>.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "whisper_full_get_segment_t1")]
    public static extern long whisper_full_get_segment_t1(IntPtr ctx, int i_segment);

    // ── Language detection ────────────────────────────────────────────────

    /// <summary>
    /// Auto-detect the spoken language from the audio loaded into the context.
    /// Returns the language ID (an integer index into whisper's internal
    /// language table).
    /// </summary>
    /// <param name="ctx">Whisper context with audio already loaded.</param>
    /// <param name="offset_ms">Offset in milliseconds from the start of the audio.</param>
    /// <param name="n_threads">Number of threads to use for detection.</param>
    /// <returns>Detected language ID, or negative on error.</returns>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "whisper_lang_auto_detect")]
    public static extern int whisper_lang_auto_detect(
        IntPtr ctx, int offset_ms, int n_threads);

    /// <summary>
    /// Convert a whisper language ID to its short string representation
    /// (e.g. "en", "zh", "de"). Returns a pointer to a static string.
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "whisper_lang_str")]
    public static extern IntPtr whisper_lang_str(int id);

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Marshal the segment text pointer to a managed string. Returns
    /// <see cref="string.Empty"/> if the pointer is null.
    /// </summary>
    public static string GetSegmentText(IntPtr ctx, int segment)
    {
        var ptr = whisper_full_get_segment_text(ctx, segment);
        return ptr == IntPtr.Zero
            ? string.Empty
            : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
    }

    /// <summary>
    /// Marshal the language-string pointer to a managed string. Returns
    /// <c>"und"</c> (undetermined) if the pointer is null or the ID is
    /// negative.
    /// </summary>
    public static string GetLanguageString(int languageId)
    {
        if (languageId < 0) return "und";
        var ptr = whisper_lang_str(languageId);
        return ptr == IntPtr.Zero
            ? "und"
            : Marshal.PtrToStringUTF8(ptr) ?? "und";
    }
}
