using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Embeddings
{
    public class Phi3MiniTextEmbedder : ITextEmbedder, IDisposable
    {
        private readonly object _modelLock = new();
        private bool _disposed = false;

        public Phi3MiniTextEmbedder(string modelPath)
        {
            // In a real implementation, you would load the model here
            // For now we'll just validate the files exist
            ValidateModelFiles(modelPath);
        }

        public Task<float[]> GenerateAsync(string text, CancellationToken ct = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Phi3MiniTextEmbedder));

            // In a real implementation, this would perform the actual embedding
            // For now we'll return dummy data
            var random = new Random();
            var embedding = Enumerable.Range(0, 384)
                .Select(_ => (float)random.NextDouble())
                .ToArray();

            return Task.FromResult(embedding);
        }

        private void ValidateModelFiles(string modelPath)
        {
            var requiredFiles = new[]
            {
                "pytorch_model.bin",
                "config.json",
                "tokenizer.json"
            };

            foreach (var file in requiredFiles)
            {
                if (!File.Exists(Path.Combine(modelPath, file)))
                {
                    throw new FileNotFoundException($"Required model file {file} not found in {modelPath}");
                }
            }
        }

        public void Dispose()
        {
            lock (_modelLock)
            {
                if (!_disposed)
                {
                    // Clean up any resources here
                    _disposed = true;
                }
            }
        }
    }
}