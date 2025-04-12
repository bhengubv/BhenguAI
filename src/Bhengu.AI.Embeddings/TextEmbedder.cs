using Bhengu.AI.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Embeddings
{
    public sealed class TextEmbedder : IDisposable
    {
        private readonly IModelManager _modelManager;
        private readonly byte[] _expectedChecksum;
        private SafeModelHandle? _model;
        private readonly object _modelLock = new();

        // Fixed constructor
        public TextEmbedder(IModelManager modelManager, byte[] expectedChecksum)
        {
            _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
            _expectedChecksum = expectedChecksum ?? throw new ArgumentNullException(nameof(expectedChecksum));
        }

        public async Task<float[]> GenerateAsync(string text, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Text cannot be empty", nameof(text));

            var path = await _modelManager.GetModelPathAsync("miniLM", ct).ConfigureAwait(false);
            await LoadModelAsync(path, ct).ConfigureAwait(false);

            return PlatformInterop.GenerateEmbedding(_model!, text); // Safe after LoadModel
        }

        private async Task LoadModelAsync(string path, CancellationToken ct)
        {
            if (!await _modelManager.VerifyModelAsync(path, _expectedChecksum, ct).ConfigureAwait(false))
                throw new InvalidDataException("Model checksum verification failed");

            lock (_modelLock)
            {
                _model?.Dispose();
                _model = PlatformInterop.LoadModel(path);
            }
        }

        public void Dispose()
        {
            _model?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}