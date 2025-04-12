using Bhengu.AI.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Bhengu.AI.Embeddings
{
    public sealed class MiniLMEmbeddingService : IDisposable
    {
        private InferenceSession? _session;
        private readonly IModelLoader _modelLoader;
        private bool _disposed;

        public int EmbeddingSize => 384;

        public MiniLMEmbeddingService(IModelLoader modelLoader)
        {
            _modelLoader = modelLoader ?? throw new ArgumentNullException(nameof(modelLoader));
        }

        public async Task InitializeAsync()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MiniLMEmbeddingService));
            if (_session != null) return;

            var modelPath = await _modelLoader.DownloadModelAsync("miniLM");
            _session = new InferenceSession(modelPath);
        }

        public float[] GenerateEmbedding(string text)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MiniLMEmbeddingService));
            if (_session == null) throw new InvalidOperationException("Model not initialized");
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Text cannot be empty");

            var inputTensor = new DenseTensor<string>(new[] { text }, new[] { 1 });
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", inputTensor)
            };

            using var results = _session.Run(inputs);
            return results.First().AsTensor<float>().ToArray();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _session?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}