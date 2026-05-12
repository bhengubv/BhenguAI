// TextEmbedder.cs
//
// On-device text embedding using a GGUF embedding model via llama.cpp.
// Supports any llama.cpp-compatible embedding model (Qwen-Embedding, BGE-zh,
// nomic-embed, etc.). The backend is factory-injectable for testability.
//
// Architecture notes:
//   - LlamaEmbeddingBackend is the production path. It loads a GGUF embedding
//     model with embeddings=1 on the context, runs llama_decode, then reads
//     the pooled embedding vector via llama_get_embeddings_seq.
//   - L2 normalisation is always applied so downstream cosine similarity
//     reduces to a dot product.
//   - TextEmbedder is disposable; it destroys the native model handle when
//     disposed.
//   - Model loading is lazy (first GenerateAsync call) and serialised by a
//     SemaphoreSlim so concurrent callers share a single initialisation.

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Circle.AI.Core;
using Circle.AI.Inference;

namespace Circle.AI.Embeddings
{
    // -------------------------------------------------------------------------
    // Internal embedding-backend abstraction — lets tests inject a fake
    // without needing the native llama.cpp library on the test machine.
    // -------------------------------------------------------------------------

    internal interface IEmbeddingBackend : IDisposable
    {
        /// <summary>Number of floats returned by <see cref="Embed"/>.</summary>
        int Dimension { get; }

        /// <summary>
        /// Embeds <paramref name="text"/> and returns a L2-normalised vector.
        /// Not thread-safe — the caller (<see cref="TextEmbedder"/>) serialises
        /// with a semaphore.
        /// </summary>
        float[] Embed(string text);
    }

    // -------------------------------------------------------------------------
    // Production backend — llama.cpp GGUF embedding model
    // -------------------------------------------------------------------------

    internal sealed class LlamaEmbeddingBackend : IEmbeddingBackend
    {
        private readonly LlamaModelHandle _model;
        private readonly int _dimension;
        private readonly int _threads;
        private bool _disposed;

        private static int s_backendRefCount;
        private static readonly object s_lock = new();

        public LlamaEmbeddingBackend(string modelPath, int? threads = null)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                throw new ArgumentException("Model path is required.", nameof(modelPath));
            if (!System.IO.File.Exists(modelPath))
                throw new System.IO.FileNotFoundException("Embedding model file not found.", modelPath);

            EnsureBackend();

            var mp = LlamaCppInterop.llama_model_default_params();
            mp.use_mmap = 1;
            mp.use_mlock = 0;
            mp.n_gpu_layers = 0;

            var handle = LlamaCppInterop.llama_model_load_from_file(modelPath, mp);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                ReleaseBackend();
                throw new InvalidOperationException(
                    $"llama.cpp failed to load embedding model at '{modelPath}'.");
            }

            _model = handle;
            _dimension = LlamaCppInterop.llama_n_embd(_model);
            _threads = threads ?? Math.Max(1, Environment.ProcessorCount);

            if (_dimension <= 0)
                throw new InvalidOperationException(
                    "Embedding model returned dimension <= 0. " +
                    "Ensure the GGUF file is a valid embedding model.");
        }

        public int Dimension => _dimension;

        public unsafe float[] Embed(string text)
        {
            ThrowIfDisposed();

            // 1. Create an embeddings-mode context per call (cheap; no KV cache).
            var cp = LlamaCppInterop.llama_context_default_params();
            cp.n_ctx = 512;                // typical max sequence for embedding models
            cp.n_batch = 512;
            cp.n_ubatch = 512;
            cp.n_threads = _threads;
            cp.n_threads_batch = _threads;
            cp.embeddings = 1;             // ← enables embedding output
            cp.pooling_type = 1;           // LLAMA_POOLING_TYPE_MEAN

            using var ctx = LlamaCppInterop.llama_new_context_with_model(_model, cp);
            if (ctx.IsInvalid)
                throw new InvalidOperationException("llama.cpp failed to create embedding context.");

            // 2. Tokenise the input.
            var utf8 = Encoding.UTF8.GetBytes(text);
            int maxTokens = utf8.Length + 8;
            var tokens = new int[maxTokens];

            int nTokens;
            fixed (byte* pText = utf8)
            fixed (int* pTok = tokens)
            {
                nTokens = LlamaCppInterop.llama_tokenize(
                    _model, pText, utf8.Length,
                    pTok, tokens.Length,
                    add_special: 1,   // prepend BOS
                    parse_special: 0);
            }

            if (nTokens < 0)
            {
                // Resize and retry.
                tokens = new int[-nTokens];
                fixed (byte* pText = utf8)
                fixed (int* pTok = tokens)
                {
                    nTokens = LlamaCppInterop.llama_tokenize(
                        _model, pText, utf8.Length,
                        pTok, tokens.Length,
                        add_special: 1, parse_special: 0);
                }
                if (nTokens < 0)
                    throw new InvalidOperationException("Tokenisation of embedding input failed.");
            }

            // Truncate to the context window.
            nTokens = Math.Min(nTokens, (int)cp.n_ctx);

            // 3. Decode the prompt batch.
            fixed (int* pTok = tokens)
            {
                var batch = LlamaCppInterop.llama_batch_get_one(pTok, nTokens);
                int rc = LlamaCppInterop.llama_decode(ctx, batch);
                if (rc != 0)
                    throw new InvalidOperationException(
                        $"llama_decode for embedding failed with code {rc}.");
            }

            // 4. Read the pooled embedding vector.
            // llama_get_embeddings_seq returns the mean-pooled vector for seq 0.
            IntPtr ptr = LlamaCppInterop.llama_get_embeddings_seq(ctx, 0);
            if (ptr == IntPtr.Zero)
            {
                // Fall back to the non-seq variant (older llama.cpp builds).
                ptr = LlamaCppInterop.llama_get_embeddings(ctx);
            }
            if (ptr == IntPtr.Zero)
                throw new InvalidOperationException(
                    "llama_get_embeddings returned null. Ensure the model supports embedding output.");

            var result = new float[_dimension];
            Marshal.Copy(ptr, result, 0, _dimension);

            // 5. L2-normalise so cosine similarity == dot product downstream.
            L2Normalize(result);
            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _model.Dispose();
            ReleaseBackend();
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LlamaEmbeddingBackend));
        }

        private static void EnsureBackend()
        {
            lock (s_lock)
            {
                if (s_backendRefCount == 0) LlamaCppInterop.llama_backend_init();
                s_backendRefCount++;
            }
        }

        private static void ReleaseBackend()
        {
            lock (s_lock)
            {
                if (s_backendRefCount > 0)
                {
                    s_backendRefCount--;
                    if (s_backendRefCount == 0) LlamaCppInterop.llama_backend_free();
                }
            }
        }

        private static void L2Normalize(float[] v)
        {
            double norm = 0.0;
            foreach (var x in v) norm += (double)x * x;
            norm = Math.Sqrt(norm);
            if (norm < 1e-12) return; // zero vector — leave as-is
            float scale = (float)(1.0 / norm);
            for (int i = 0; i < v.Length; i++) v[i] *= scale;
        }
    }

    // -------------------------------------------------------------------------
    // Public TextEmbedder — thin orchestration shell over IEmbeddingBackend
    // -------------------------------------------------------------------------

    /// <summary>
    /// On-device text embedder backed by a GGUF embedding model (Qwen-Embedding,
    /// BGE, nomic-embed, etc.) loaded via llama.cpp. Returns L2-normalised
    /// <c>float[]</c> vectors suitable for cosine-similarity retrieval.
    /// </summary>
    public sealed class TextEmbedder : ITextEmbedder, IDisposable
    {
        private readonly IModelManager _modelManager;
        private readonly byte[] _expectedChecksum;
        private readonly Func<string, IEmbeddingBackend> _backendFactory;

        private IEmbeddingBackend? _backend;
        private readonly SemaphoreSlim _initGate = new(1, 1);
        private bool _disposed;

        // ------------------------------------------------------------------
        // Public constructors
        // ------------------------------------------------------------------

        /// <summary>
        /// Production constructor. Uses <see cref="LlamaEmbeddingBackend"/>
        /// with the model path resolved via <paramref name="modelManager"/>.
        /// </summary>
        public TextEmbedder(IModelManager modelManager, byte[] expectedChecksum)
            : this(modelManager, expectedChecksum,
                  static path => new LlamaEmbeddingBackend(path))
        { }

        // ------------------------------------------------------------------
        // Internal constructor for testing (inject a fake backend)
        // ------------------------------------------------------------------

        internal TextEmbedder(
            IModelManager modelManager,
            byte[] expectedChecksum,
            Func<string, IEmbeddingBackend> backendFactory)
        {
            _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
            _expectedChecksum = expectedChecksum ?? throw new ArgumentNullException(nameof(expectedChecksum));
            _backendFactory = backendFactory ?? throw new ArgumentNullException(nameof(backendFactory));
        }

        // ------------------------------------------------------------------
        // ITextEmbedder
        // ------------------------------------------------------------------

        /// <inheritdoc />
        public async Task<float[]> GenerateAsync(string text, CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TextEmbedder));
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Text cannot be empty.", nameof(text));

            var backend = await EnsureBackendAsync(ct).ConfigureAwait(false);

            // Embed is CPU-bound; run on thread pool so callers aren't blocked.
            return await Task.Run(() => backend.Embed(text), ct).ConfigureAwait(false);
        }

        // ------------------------------------------------------------------
        // Dispose
        // ------------------------------------------------------------------

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _backend?.Dispose();
            _initGate.Dispose();
            GC.SuppressFinalize(this);
        }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------

        private async Task<IEmbeddingBackend> EnsureBackendAsync(CancellationToken ct)
        {
            if (_backend is not null) return _backend;

            await _initGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_backend is not null) return _backend;

                // Resolve + verify model path via the IModelManager contract.
                var path = await _modelManager
                    .GetModelPathAsync("embedding", ct)
                    .ConfigureAwait(false);

                var verified = await _modelManager
                    .VerifyModelAsync(path, _expectedChecksum, ct)
                    .ConfigureAwait(false);

                if (!verified)
                    throw new InvalidDataException(
                        "Embedding model checksum verification failed. " +
                        "The file may be corrupt or tampered with.");

                _backend = _backendFactory(path);
                return _backend;
            }
            finally
            {
                _initGate.Release();
            }
        }
    }
}
