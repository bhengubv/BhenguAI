// LlamaCppInterop.cs
//
// P/Invoke bindings for llama.cpp's C API. Uses [LibraryImport] (the .NET 8+
// source-generated marshaller) for AOT-friendly P/Invoke.
//
// IMPORTANT: The native library must be present alongside the .NET binary at
// runtime. Library names by platform:
//   - Windows  -> llama.dll
//   - Linux    -> libllama.so
//   - Android  -> libllama.so (must be shipped inside the APK's lib/<abi>/)
//   - macOS    -> libllama.dylib
//   - iOS      -> libllama.dylib (or statically linked into __Internal)
//
// See SETUP.md for instructions on obtaining or building the native binaries
// for each target platform.
//
// We bind both classic and modern names where llama.cpp evolved its API
// (e.g. llama_load_model_from_file -> llama_model_load_from_file). The wrapper
// methods below are the public surface; the raw [LibraryImport] entries are
// kept private.

using System;
using System.Runtime.InteropServices;

namespace Circle.AI.Inference;

/// <summary>
/// Native handle wrapping a <c>llama_model*</c>. Released with
/// <see cref="LlamaCppInterop.llama_model_free"/>.
/// </summary>
internal sealed class LlamaModelHandle : SafeHandle
{
    public LlamaModelHandle() : base(IntPtr.Zero, ownsHandle: true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        if (handle != IntPtr.Zero)
        {
            LlamaCppInterop.llama_model_free(handle);
            handle = IntPtr.Zero;
        }
        return true;
    }
}

/// <summary>
/// Native handle wrapping a <c>llama_context*</c>. Released with
/// <see cref="LlamaCppInterop.llama_free"/>.
/// </summary>
internal sealed class LlamaContextHandle : SafeHandle
{
    public LlamaContextHandle() : base(IntPtr.Zero, ownsHandle: true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        if (handle != IntPtr.Zero)
        {
            LlamaCppInterop.llama_free(handle);
            handle = IntPtr.Zero;
        }
        return true;
    }
}

/// <summary>
/// Mirrors <c>llama_model_params</c> from llama.cpp. The exact field layout
/// must track upstream; this matches a recent (b3000+) snapshot.
/// Bool fields are represented as <see cref="byte"/> so the struct remains
/// blittable under <c>DisableRuntimeMarshalling</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LlamaModelParams
{
    public IntPtr devices;                          // ggml_backend_dev_t* (nullable)
    public IntPtr tensor_buft_overrides;            // const llama_model_tensor_buft_override* (nullable)
    public int n_gpu_layers;                        // number of layers offloaded to GPU
    public int split_mode;                          // enum llama_split_mode
    public int main_gpu;
    public IntPtr tensor_split;                     // const float* (nullable)
    public IntPtr progress_callback;                // llama_progress_callback (nullable)
    public IntPtr progress_callback_user_data;
    public IntPtr kv_overrides;                     // const llama_model_kv_override* (nullable)
    public byte vocab_only;
    public byte use_mmap;
    public byte use_mlock;
    public byte check_tensors;
}

/// <summary>
/// Mirrors <c>llama_context_params</c> from llama.cpp. Populated via
/// <see cref="LlamaCppInterop.llama_context_default_params"/> then mutated.
/// Bool fields are stored as <see cref="byte"/> for blittability.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LlamaContextParams
{
    public uint n_ctx;
    public uint n_batch;
    public uint n_ubatch;
    public uint n_seq_max;
    public int  n_threads;
    public int  n_threads_batch;

    public int rope_scaling_type;
    public int pooling_type;
    public int attention_type;

    public float rope_freq_base;
    public float rope_freq_scale;
    public float yarn_ext_factor;
    public float yarn_attn_factor;
    public float yarn_beta_fast;
    public float yarn_beta_slow;
    public uint  yarn_orig_ctx;
    public float defrag_thold;

    public IntPtr cb_eval;
    public IntPtr cb_eval_user_data;

    public int type_k;
    public int type_v;

    public byte logits_all;
    public byte embeddings;
    public byte offload_kqv;
    public byte flash_attn;
    public byte no_perf;

    public IntPtr abort_callback;
    public IntPtr abort_callback_data;
}

/// <summary>
/// Mirrors <c>llama_batch</c> — a thin descriptor around tokens to decode.
/// Pointers point into managed/native scratch buffers held alive by the caller
/// for the duration of the <c>llama_decode</c> call.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LlamaBatch
{
    public int n_tokens;
    public IntPtr token;     // llama_token*
    public IntPtr embd;      // float* (only if embeddings)
    public IntPtr pos;       // llama_pos*
    public IntPtr n_seq_id;  // int32_t*
    public IntPtr seq_id;    // llama_seq_id**
    public IntPtr logits;    // int8_t*
}

/// <summary>
/// P/Invoke entry points for llama.cpp's C API. Internal-only — callers should
/// use <c>QwenTextGenerator</c> rather than touching these directly.
/// </summary>
/// <remarks>
/// <para>
/// The native library (<c>llama.dll</c> / <c>libllama.so</c> /
/// <c>libllama.dylib</c>) must be present in the same directory as the
/// running .NET assembly, or somewhere on the platform's library search path.
/// See <c>SETUP.md</c> for build/download instructions.
/// </para>
/// <para>
/// The <see cref="DefaultDllImportSearchPaths"/> attribute is applied at the
/// assembly level (see <c>AssemblyInfo.cs</c>) so that all P/Invokes in this
/// assembly resolve native libs from the assembly's own directory first.
/// </para>
/// </remarks>
internal static partial class LlamaCppInterop
{
    /// <summary>
    /// Resolved library name. Windows uses <c>llama.dll</c>; Linux/Android use
    /// <c>libllama.so</c>; macOS/iOS use <c>libllama.dylib</c>.
    /// </summary>
    public const string LibraryName = "llama";

    // -- backend lifecycle ------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "llama_backend_init")]
    public static partial void llama_backend_init();

    [LibraryImport(LibraryName, EntryPoint = "llama_backend_free")]
    public static partial void llama_backend_free();

    // -- default param structs -------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "llama_model_default_params")]
    public static partial LlamaModelParams llama_model_default_params();

    [LibraryImport(LibraryName, EntryPoint = "llama_context_default_params")]
    public static partial LlamaContextParams llama_context_default_params();

    // -- model load / free -----------------------------------------------

    /// <summary>
    /// Loads a GGUF model from disk. Returns an invalid handle on failure.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "llama_model_load_from_file", StringMarshalling = StringMarshalling.Utf8)]
    public static partial LlamaModelHandle llama_model_load_from_file(
        string path_model,
        LlamaModelParams @params);

    [LibraryImport(LibraryName, EntryPoint = "llama_model_free")]
    public static partial void llama_model_free(IntPtr model);

    // -- context create / free -------------------------------------------

    /// <summary>
    /// Creates a new inference context attached to the given model.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "llama_new_context_with_model")]
    public static partial LlamaContextHandle llama_new_context_with_model(
        LlamaModelHandle model,
        LlamaContextParams @params);

    [LibraryImport(LibraryName, EntryPoint = "llama_free")]
    public static partial void llama_free(IntPtr ctx);

    // -- context introspection -------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "llama_n_ctx")]
    public static partial uint llama_n_ctx(LlamaContextHandle ctx);

    [LibraryImport(LibraryName, EntryPoint = "llama_n_vocab")]
    public static partial int llama_n_vocab(LlamaModelHandle model);

    [LibraryImport(LibraryName, EntryPoint = "llama_get_model")]
    public static partial IntPtr llama_get_model(LlamaContextHandle ctx);

    // -- special tokens ---------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "llama_token_eos")]
    public static partial int llama_token_eos(LlamaModelHandle model);

    [LibraryImport(LibraryName, EntryPoint = "llama_token_bos")]
    public static partial int llama_token_bos(LlamaModelHandle model);

    [LibraryImport(LibraryName, EntryPoint = "llama_token_nl")]
    public static partial int llama_token_nl(LlamaModelHandle model);

    // -- tokenisation -----------------------------------------------------

    /// <summary>
    /// Tokenises UTF-8 input into <paramref name="tokens"/>. Returns the
    /// number of tokens written, or a negative value if the buffer was too
    /// small (in which case <c>-result</c> is the required size).
    /// Bool flags are passed as bytes (0 = false, non-zero = true) for
    /// blittability under <c>DisableRuntimeMarshalling</c>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "llama_tokenize")]
    public static unsafe partial int llama_tokenize(
        LlamaModelHandle model,
        byte* text,
        int text_len,
        int* tokens,
        int n_tokens_max,
        byte add_special,
        byte parse_special);

    /// <summary>
    /// Converts a token id into its UTF-8 byte representation, written into
    /// <paramref name="buf"/>. Returns the number of bytes written (or a
    /// negative value indicating the required buffer size).
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "llama_token_to_piece")]
    public static unsafe partial int llama_token_to_piece(
        LlamaModelHandle model,
        int token,
        byte* buf,
        int length,
        int lstrip,
        byte special);

    // -- batch / decode ---------------------------------------------------

    /// <summary>
    /// Builds a single-sequence batch from a token array. The returned struct
    /// borrows pointers from native scratch buffers managed by llama.cpp; it
    /// is valid until the next call to <c>llama_batch_get_one</c> or until
    /// <c>llama_decode</c> consumes it.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "llama_batch_get_one")]
    public static unsafe partial LlamaBatch llama_batch_get_one(
        int* tokens,
        int n_tokens);

    [LibraryImport(LibraryName, EntryPoint = "llama_decode")]
    public static partial int llama_decode(
        LlamaContextHandle ctx,
        LlamaBatch batch);

    /// <summary>
    /// Returns a pointer to the per-token logits for the last decoded token
    /// (or for every token, if <c>logits_all</c> was set on the context).
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "llama_get_logits")]
    public static partial IntPtr llama_get_logits(LlamaContextHandle ctx);

    [LibraryImport(LibraryName, EntryPoint = "llama_get_logits_ith")]
    public static partial IntPtr llama_get_logits_ith(LlamaContextHandle ctx, int i);

    // -- embeddings ------------------------------------------------------

    /// <summary>
    /// Returns the embedding dimension for the loaded model (e.g. 1536 for
    /// Qwen-Embedding-0.6B). Only valid when the context was created with
    /// <c>LlamaContextParams.embeddings = 1</c>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "llama_n_embd")]
    public static partial int llama_n_embd(LlamaModelHandle model);

    /// <summary>
    /// Returns a pointer to the last-sequence embedding vector (float[n_embd]).
    /// Only valid after a successful <c>llama_decode</c> with
    /// <c>LlamaContextParams.embeddings = 1</c>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "llama_get_embeddings")]
    public static partial IntPtr llama_get_embeddings(LlamaContextHandle ctx);

    /// <summary>
    /// Returns a pointer to the pooled embedding for sequence <paramref name="seq_id"/>.
    /// Preferred over <see cref="llama_get_embeddings"/> for multi-sequence batches.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "llama_get_embeddings_seq")]
    public static partial IntPtr llama_get_embeddings_seq(LlamaContextHandle ctx, int seq_id);

    // -- sampling --------------------------------------------------------
    //
    // llama.cpp's sampler API has changed shape several times. We expose the
    // pre-b3000 "greedy" entry point as well as the modern sampler-chain
    // entry points; QwenTextGenerator picks whichever it finds available.

    /// <summary>
    /// Legacy greedy sampler. Picks the argmax token over the most recent
    /// logits. Present in older llama.cpp builds; modern builds use
    /// <c>llama_sampler_*</c> instead.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "llama_sample_token_greedy")]
    public static partial int llama_sample_token_greedy(
        LlamaContextHandle ctx,
        IntPtr candidates);

    /// <summary>
    /// Modern sampler-chain factory (b3000+).
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "llama_sampler_chain_init")]
    public static partial IntPtr llama_sampler_chain_init(LlamaSamplerChainParams @params);

    [LibraryImport(LibraryName, EntryPoint = "llama_sampler_chain_default_params")]
    public static partial LlamaSamplerChainParams llama_sampler_chain_default_params();

    [LibraryImport(LibraryName, EntryPoint = "llama_sampler_chain_add")]
    public static partial void llama_sampler_chain_add(IntPtr chain, IntPtr sampler);

    [LibraryImport(LibraryName, EntryPoint = "llama_sampler_init_greedy")]
    public static partial IntPtr llama_sampler_init_greedy();

    [LibraryImport(LibraryName, EntryPoint = "llama_sampler_init_temp")]
    public static partial IntPtr llama_sampler_init_temp(float t);

    [LibraryImport(LibraryName, EntryPoint = "llama_sampler_init_top_k")]
    public static partial IntPtr llama_sampler_init_top_k(int k);

    [LibraryImport(LibraryName, EntryPoint = "llama_sampler_init_top_p")]
    public static partial IntPtr llama_sampler_init_top_p(float p, nuint min_keep);

    [LibraryImport(LibraryName, EntryPoint = "llama_sampler_init_dist")]
    public static partial IntPtr llama_sampler_init_dist(uint seed);

    [LibraryImport(LibraryName, EntryPoint = "llama_sampler_sample")]
    public static partial int llama_sampler_sample(
        IntPtr smpl,
        LlamaContextHandle ctx,
        int idx);

    [LibraryImport(LibraryName, EntryPoint = "llama_sampler_accept")]
    public static partial void llama_sampler_accept(IntPtr smpl, int token);

    [LibraryImport(LibraryName, EntryPoint = "llama_sampler_free")]
    public static partial void llama_sampler_free(IntPtr smpl);

    /// <summary>
    /// Mirrors <c>llama_sampler_chain_params</c>. <c>no_perf</c> is stored as
    /// a byte for blittability.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LlamaSamplerChainParams
    {
        public byte no_perf;
    }

    // ── Session / KV-cache state ──────────────────────────────────────────────

    private const string LlamaDll = "llama";

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint llama_state_get_size(nint ctx);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint llama_state_get_data(nint ctx, byte[] dst, nuint size);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint llama_state_set_data(nint ctx, byte[] src, nuint size);

    // C bool is a single byte under DisableRuntimeMarshalling; return byte and compare != 0.
    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern byte llama_state_save_file(
        nint ctx,
        string pathSession,
        int[] tokens,
        nuint nTokenCount);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern byte llama_state_load_file(
        nint ctx,
        string pathSession,
        int[] tokensOut,
        nuint nTokenCapacity,
        out nuint nTokenCountOut);

    /// <summary>Save the KV-cache session to <paramref name="path"/>.</summary>
    /// <returns>true on success.</returns>
    public static bool SaveSession(nint ctx, string path, int[] tokens)
        => llama_state_save_file(ctx, path, tokens, (nuint)tokens.Length) != 0;

    /// <summary>Load a KV-cache session from <paramref name="path"/>.</summary>
    /// <param name="tokenCapacity">Maximum number of tokens to restore.</param>
    /// <param name="tokensRestored">Actual tokens written to <paramref name="tokensOut"/>.</param>
    /// <returns>true on success.</returns>
    public static bool LoadSession(
        nint ctx,
        string path,
        int[] tokensOut,
        out nuint tokensRestored)
        => llama_state_load_file(ctx, path, tokensOut, (nuint)tokensOut.Length, out tokensRestored) != 0;

    // ── Vision / llava ───────────────────────────────────────────────────────

    /// <summary>Name of the llava clip shared library.</summary>
    private const string LlavaDll = "llava";

    [DllImport(LlavaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint llava_image_embed_make_with_bytes(
        nint ctxClip,
        int nThreads,
        byte[] imageData,
        int imageBytes);

    [DllImport(LlavaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llava_image_embed_free(nint embed);

    // C bool returned as byte under DisableRuntimeMarshalling.
    [DllImport(LlavaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern byte llava_eval_image_embed(
        nint ctxLlama,
        nint embed,
        int nBatch,
        ref int nPast);
}
