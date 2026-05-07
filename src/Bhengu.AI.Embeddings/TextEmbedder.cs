using Bhengu.AI.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Embeddings
{
    /// <summary>
    /// Placeholder embedder. The previous implementation depended on a US-origin embedding model
    /// (MiniLM via ONNX Runtime). Pending replacement with a sovereign-origin embedder
    /// (e.g. BGE-zh, Qwen-Embedding, or similar Chinese-origin model). See TODO.md.
    /// </summary>
    public sealed class TextEmbedder : IDisposable
    {
        private readonly IModelManager _modelManager;
        private readonly byte[] _expectedChecksum;
        private bool _disposed;

        public TextEmbedder(IModelManager modelManager, byte[] expectedChecksum)
        {
            _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
            _expectedChecksum = expectedChecksum ?? throw new ArgumentNullException(nameof(expectedChecksum));
        }

        public Task<float[]> GenerateAsync(string text, CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TextEmbedder));
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Text cannot be empty", nameof(text));

            throw new NotImplementedException(
                "Embeddings backend not yet wired. Pending sovereign-origin embedding model. See TODO.md.");
        }

        public void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
