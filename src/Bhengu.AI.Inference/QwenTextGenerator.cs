// QwenTextGenerator.cs
//
// IChatGenerator backed by llama.cpp running a Qwen-family GGUF model
// (Qwen 3 14B is the design target, but anything using the Qwen ChatML
// template will work).
//
// Design notes:
//   - We pin the model handle for the lifetime of the generator. Each
//     GenerateAsync / StreamAsync call gets its own context so calls can run
//     concurrently against the same underlying weights.
//   - Sampling uses llama.cpp's modern sampler-chain API (b3000+). The legacy
//     greedy entry point is left bound in LlamaCppInterop for fallback work
//     when we layer in PowerInfer-2.
//   - All native pointers are encapsulated — callers never see IntPtr.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Bhengu.AI.Inference;

/// <summary>
/// On-device chat generator backed by llama.cpp running a Qwen GGUF model.
/// </summary>
public sealed class QwenTextGenerator : IChatGenerator
{
    // ChatML role tags used by Qwen 1.5 / 2 / 3 / Qwen-VL family.
    private const string ImStart = "<|im_start|>";
    private const string ImEnd   = "<|im_end|>";

    private static readonly string[] DefaultStopSequences = [ImEnd, ImStart];

    private readonly LlamaModelHandle _model;
    private readonly uint _contextSize;
    private readonly int _threads;

    private static int s_backendRefCount;
    private static readonly object s_backendStaticLock = new();

    private bool _disposed;

    /// <summary>
    /// Loads a GGUF model from disk and prepares it for generation.
    /// </summary>
    /// <param name="modelPath">Absolute path to the <c>.gguf</c> file.</param>
    /// <param name="contextSize">
    /// Maximum context window in tokens. Defaults to 4096; raise for longer
    /// conversations at the cost of RAM.
    /// </param>
    /// <param name="threads">
    /// Number of CPU threads for decode. <c>null</c> lets llama.cpp pick a
    /// default (usually <c>Environment.ProcessorCount</c>).
    /// </param>
    /// <exception cref="ArgumentException">Path is null or empty.</exception>
    /// <exception cref="System.IO.FileNotFoundException">Model file missing.</exception>
    /// <exception cref="InvalidOperationException">Native load failed.</exception>
    public QwenTextGenerator(string modelPath, uint contextSize = 4096, int? threads = null)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            throw new ArgumentException("Model path is required.", nameof(modelPath));

        if (!System.IO.File.Exists(modelPath))
            throw new System.IO.FileNotFoundException("GGUF model file not found.", modelPath);

        if (contextSize == 0)
            throw new ArgumentOutOfRangeException(nameof(contextSize), "Context size must be > 0.");

        EnsureBackendInitialised();

        var modelParams = LlamaCppInterop.llama_model_default_params();
        // Default to CPU-only on the assumption that on-device runs without a
        // configured CUDA / Metal backend. Hosting layers can override later
        // by calling into LlamaCppInterop directly if they wire up a build
        // with GPU support.
        modelParams.n_gpu_layers = 0;
        modelParams.use_mmap = 1;
        modelParams.use_mlock = 0;

        var handle = LlamaCppInterop.llama_model_load_from_file(modelPath, modelParams);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            ReleaseBackendIfLast();
            throw new InvalidOperationException(
                $"llama.cpp failed to load model at '{modelPath}'. " +
                "Verify the file is a valid GGUF and that the native llama " +
                "library is on the search path.");
        }

        _model = handle;
        _contextSize = contextSize;
        _threads = threads ?? Math.Max(1, Environment.ProcessorCount);
    }

    /// <inheritdoc />
    public async Task<string> GenerateAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ThrowIfDisposed();

        var sb = new StringBuilder();
        await foreach (var piece in StreamAsync(messages, options, ct).ConfigureAwait(false))
        {
            sb.Append(piece);
        }
        return sb.ToString();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ThrowIfDisposed();

        options ??= new GenerationOptions();

        // Hand the heavy lifting off to a worker thread; pipe pieces back via
        // a channel so the async iterator stays cheap.
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

        var prompt = BuildQwenChatPrompt(messages);
        var stopSequences = (options.StopSequences is { Length: > 0 }
            ? options.StopSequences
            : DefaultStopSequences);

        var work = Task.Run(() =>
        {
            try
            {
                RunGeneration(prompt, options, stopSequences, channel.Writer, ct);
                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException oce)
            {
                channel.Writer.TryComplete(oce);
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }, ct);

        await foreach (var piece in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return piece;
        }

        // Surface any exception caught inside the worker (Channel completes
        // with the original exception, but await it for symmetry).
        await work.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _model.Dispose();
        ReleaseBackendIfLast();
        GC.SuppressFinalize(this);
    }

    // ------------------------------------------------------------------
    // internals
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds a Qwen-style ChatML prompt. System / user / assistant turns are
    /// each wrapped in <c>&lt;|im_start|&gt;role\n…\n&lt;|im_end|&gt;\n</c>,
    /// and the final assistant turn is left open for the model to complete.
    /// </summary>
    internal static string BuildQwenChatPrompt(IReadOnlyList<ChatMessage> messages)
    {
        var sb = new StringBuilder(messages.Count * 64);
        foreach (var m in messages)
        {
            var role = string.IsNullOrWhiteSpace(m.Role) ? "user" : m.Role.Trim().ToLowerInvariant();
            sb.Append(ImStart).Append(role).Append('\n');
            sb.Append(m.Content ?? string.Empty);
            sb.Append('\n').Append(ImEnd).Append('\n');
        }
        sb.Append(ImStart).Append("assistant\n");
        return sb.ToString();
    }

    private void RunGeneration(
        string prompt,
        GenerationOptions options,
        string[] stopSequences,
        ChannelWriter<string> writer,
        CancellationToken ct)
    {
        // 1. Spin up a fresh context so concurrent callers don't share KV cache.
        var ctxParams = LlamaCppInterop.llama_context_default_params();
        ctxParams.n_ctx = _contextSize;
        ctxParams.n_batch = Math.Min(512u, _contextSize);
        ctxParams.n_threads = _threads;
        ctxParams.n_threads_batch = _threads;
        ctxParams.logits_all = 0;
        ctxParams.embeddings = 0;

        using var ctx = LlamaCppInterop.llama_new_context_with_model(_model, ctxParams);
        if (ctx.IsInvalid)
            throw new InvalidOperationException("llama.cpp failed to create inference context.");

        // 2. Build a sampler chain matching the requested options.
        var sampler = BuildSamplerChain(options);
        try
        {
            // 3. Tokenise the prompt.
            var promptTokens = TokeniseUtf8(prompt, addSpecial: true, parseSpecial: true);
            if (promptTokens.Length == 0)
                throw new InvalidOperationException("Tokenisation produced zero tokens.");

            int eosToken = LlamaCppInterop.llama_token_eos(_model);

            // 4. Decode the prompt in one shot.
            DecodeBatch(ctx, promptTokens, ct);

            // 5. Generate tokens one at a time, streaming UTF-8-decoded pieces.
            var emittedSoFar = new StringBuilder();
            var byteBuffer = ArrayPool<byte>.Shared.Rent(256);
            var pendingBytes = new List<byte>(8);

            try
            {
                int generated = 0;
                int maxTokens = Math.Max(1, options.MaxTokens);

                while (generated < maxTokens)
                {
                    ct.ThrowIfCancellationRequested();

                    int tokenId = LlamaCppInterop.llama_sampler_sample(sampler, ctx, -1);
                    LlamaCppInterop.llama_sampler_accept(sampler, tokenId);

                    if (tokenId == eosToken) break;

                    // Convert token -> UTF-8 bytes -> string. Multi-byte
                    // codepoints can split across tokens, so we buffer
                    // until we have a clean prefix.
                    var pieceBytes = TokenToPieceBytes(tokenId, byteBuffer);
                    if (pieceBytes.Length > 0)
                    {
                        pendingBytes.AddRange(pieceBytes);
                        if (TryDrainUtf8(pendingBytes, out var decoded))
                        {
                            emittedSoFar.Append(decoded);

                            // Stop-sequence check: if any stop string has
                            // been emitted, truncate to before it and stop.
                            if (TryFindStopSequence(emittedSoFar, stopSequences, out int stopAt))
                            {
                                var leftover = emittedSoFar.ToString(0, stopAt);
                                var alreadyEmittedLen = emittedSoFar.Length - decoded.Length;
                                if (stopAt > alreadyEmittedLen)
                                {
                                    var lastChunk = leftover[alreadyEmittedLen..];
                                    if (lastChunk.Length > 0)
                                        writer.TryWrite(lastChunk);
                                }
                                return;
                            }

                            writer.TryWrite(decoded);
                        }
                    }

                    // Feed the sampled token back in so the next decode step
                    // sees it. We use a single-token batch.
                    DecodeSingleToken(ctx, tokenId, ct);
                    generated++;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(byteBuffer);
            }
        }
        finally
        {
            if (sampler != IntPtr.Zero)
                LlamaCppInterop.llama_sampler_free(sampler);
        }
    }

    private IntPtr BuildSamplerChain(GenerationOptions options)
    {
        var chainParams = LlamaCppInterop.llama_sampler_chain_default_params();
        var chain = LlamaCppInterop.llama_sampler_chain_init(chainParams);
        if (chain == IntPtr.Zero)
            throw new InvalidOperationException("llama.cpp failed to allocate sampler chain.");

        try
        {
            if (options.Temperature <= 0f)
            {
                // Greedy decoding — argmax of logits.
                LlamaCppInterop.llama_sampler_chain_add(chain, LlamaCppInterop.llama_sampler_init_greedy());
            }
            else
            {
                if (options.TopK > 0)
                {
                    LlamaCppInterop.llama_sampler_chain_add(chain,
                        LlamaCppInterop.llama_sampler_init_top_k(options.TopK));
                }

                if (options.TopP < 1f && options.TopP > 0f)
                {
                    LlamaCppInterop.llama_sampler_chain_add(chain,
                        LlamaCppInterop.llama_sampler_init_top_p(options.TopP, 1));
                }

                LlamaCppInterop.llama_sampler_chain_add(chain,
                    LlamaCppInterop.llama_sampler_init_temp(options.Temperature));

                // 0xFFFFFFFF tells llama.cpp to use its internal RNG when no
                // seed is supplied; otherwise we honour the caller's value.
                uint seed = options.Seed.HasValue
                    ? unchecked((uint)options.Seed.Value)
                    : 0xFFFFFFFFu;
                LlamaCppInterop.llama_sampler_chain_add(chain,
                    LlamaCppInterop.llama_sampler_init_dist(seed));
            }
        }
        catch
        {
            LlamaCppInterop.llama_sampler_free(chain);
            throw;
        }

        return chain;
    }

    private unsafe int[] TokeniseUtf8(string text, bool addSpecial, bool parseSpecial)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length == 0) return Array.Empty<int>();

        byte addSpecialByte = (byte)(addSpecial ? 1 : 0);
        byte parseSpecialByte = (byte)(parseSpecial ? 1 : 0);

        // First call: pass an over-estimate of the output buffer size.
        int initialCapacity = bytes.Length + 16;
        var tokens = new int[initialCapacity];

        int written;
        fixed (byte* pText = bytes)
        fixed (int* pTokens = tokens)
        {
            written = LlamaCppInterop.llama_tokenize(
                _model, pText, bytes.Length,
                pTokens, tokens.Length,
                addSpecialByte, parseSpecialByte);
        }

        if (written < 0)
        {
            // Negative result: -written is the required buffer size.
            int required = -written;
            tokens = new int[required];
            fixed (byte* pText = bytes)
            fixed (int* pTokens = tokens)
            {
                written = LlamaCppInterop.llama_tokenize(
                    _model, pText, bytes.Length,
                    pTokens, tokens.Length,
                    addSpecialByte, parseSpecialByte);
            }
            if (written < 0)
                throw new InvalidOperationException("llama.cpp tokenisation failed.");
        }

        if (written == tokens.Length) return tokens;
        var trimmed = new int[written];
        Array.Copy(tokens, trimmed, written);
        return trimmed;
    }

    private unsafe ReadOnlySpan<byte> TokenToPieceBytes(int token, byte[] scratch)
    {
        int written;
        fixed (byte* pBuf = scratch)
        {
            written = LlamaCppInterop.llama_token_to_piece(
                _model, token, pBuf, scratch.Length, 0, special: 0);
        }

        if (written < 0)
        {
            // Need a bigger buffer. Allocate one large enough and retry once
            // — we deliberately don't reuse the pool here because pieces over
            // 256 bytes are rare.
            int required = -written;
            var bigger = new byte[required];
            fixed (byte* pBuf = bigger)
            {
                written = LlamaCppInterop.llama_token_to_piece(
                    _model, token, pBuf, bigger.Length, 0, special: 0);
            }
            if (written < 0)
                throw new InvalidOperationException("llama_token_to_piece failed.");
            return new ReadOnlySpan<byte>(bigger, 0, written);
        }

        return new ReadOnlySpan<byte>(scratch, 0, written);
    }

    private unsafe void DecodeBatch(LlamaContextHandle ctx, int[] tokens, CancellationToken ct)
    {
        // Decode in chunks of n_batch (matches what we configured above).
        const int chunkSize = 512;
        int offset = 0;
        while (offset < tokens.Length)
        {
            ct.ThrowIfCancellationRequested();

            int take = Math.Min(chunkSize, tokens.Length - offset);
            fixed (int* pTokens = &tokens[offset])
            {
                var batch = LlamaCppInterop.llama_batch_get_one(pTokens, take);
                int rc = LlamaCppInterop.llama_decode(ctx, batch);
                if (rc != 0)
                    throw new InvalidOperationException(
                        $"llama_decode failed with code {rc} at offset {offset}.");
            }
            offset += take;
        }
    }

    private unsafe void DecodeSingleToken(LlamaContextHandle ctx, int token, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        int local = token;
        var batch = LlamaCppInterop.llama_batch_get_one(&local, 1);
        int rc = LlamaCppInterop.llama_decode(ctx, batch);
        if (rc != 0)
            throw new InvalidOperationException($"llama_decode (single token) failed with code {rc}.");
    }

    /// <summary>
    /// Drains complete UTF-8 codepoints from the head of <paramref name="pending"/>,
    /// returning the decoded string and trimming the consumed bytes. Any
    /// trailing partial codepoint stays buffered for the next call.
    /// </summary>
    internal static bool TryDrainUtf8(List<byte> pending, out string decoded)
    {
        if (pending.Count == 0)
        {
            decoded = string.Empty;
            return false;
        }

        // Find the longest prefix that's a valid UTF-8 sequence.
        int safeLen = pending.Count;
        // Walk back at most 4 bytes (max UTF-8 codepoint length) looking for
        // a continuation byte that indicates an incomplete sequence at the
        // tail.
        for (int i = pending.Count - 1; i >= 0 && i >= pending.Count - 4; i--)
        {
            byte b = pending[i];
            if ((b & 0x80) == 0)
            {
                // ASCII — clean break after this.
                break;
            }
            if ((b & 0xC0) == 0xC0)
            {
                // Start of a multi-byte sequence. Check if it's complete.
                int needed = (b & 0xE0) == 0xC0 ? 2
                          : (b & 0xF0) == 0xE0 ? 3
                          : (b & 0xF8) == 0xF0 ? 4
                          : 1;
                int have = pending.Count - i;
                if (have < needed) safeLen = i;
                break;
            }
            // Continuation byte — keep walking back.
        }

        if (safeLen == 0)
        {
            decoded = string.Empty;
            return false;
        }

        var arr = new byte[safeLen];
        pending.CopyTo(0, arr, 0, safeLen);
        pending.RemoveRange(0, safeLen);
        decoded = Encoding.UTF8.GetString(arr);
        return decoded.Length > 0;
    }

    internal static bool TryFindStopSequence(StringBuilder sb, string[] stops, out int index)
    {
        var s = sb.ToString();
        foreach (var stop in stops)
        {
            if (string.IsNullOrEmpty(stop)) continue;
            int idx = s.IndexOf(stop, StringComparison.Ordinal);
            if (idx >= 0)
            {
                index = idx;
                return true;
            }
        }
        index = -1;
        return false;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(QwenTextGenerator));
    }

    /// <summary>
    /// Calls <c>llama_backend_init</c> on first use and reference-counts so
    /// concurrent generators in the same process share one backend.
    /// </summary>
    private static void EnsureBackendInitialised()
    {
        lock (s_backendStaticLock)
        {
            if (s_backendRefCount == 0)
            {
                LlamaCppInterop.llama_backend_init();
            }
            s_backendRefCount++;
        }
    }

    private static void ReleaseBackendIfLast()
    {
        lock (s_backendStaticLock)
        {
            if (s_backendRefCount > 0)
            {
                s_backendRefCount--;
                if (s_backendRefCount == 0)
                {
                    LlamaCppInterop.llama_backend_free();
                }
            }
        }
    }
}
