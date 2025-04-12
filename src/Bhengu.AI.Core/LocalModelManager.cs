using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Core
{
    public class LocalModelManager : IDisposable
    {
        private readonly IModelDownloader? _modelDownloader;
        private readonly string _modelsDirectory;
        private bool _disposed = false;

        public LocalModelManager(Uri? modelRepositoryUrl, string modelsDirectory = "Models")
        {
            _modelsDirectory = modelsDirectory;

            if (modelRepositoryUrl != null)
            {
                _modelDownloader = new HuggingFaceModelDownloader();
            }

            if (!Directory.Exists(_modelsDirectory))
            {
                Directory.CreateDirectory(_modelsDirectory);
            }
        }

        public async Task<string> GetModelPathAsync(string modelId, byte[]? expectedChecksum = null, CancellationToken ct = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LocalModelManager));

            var modelPath = Path.Combine(_modelsDirectory, SanitizeModelId(modelId));

            // If model doesn't exist locally, download it
            if (!Directory.Exists(modelPath) || !File.Exists(Path.Combine(modelPath, "pytorch_model.bin")))
            {
                if (_modelDownloader == null)
                {
                    throw new InvalidOperationException("Model not found and no downloader configured");
                }

                await _modelDownloader.DownloadModelAsync(modelId, modelPath, ct);
            }

            // Verify checksum if provided
            if (expectedChecksum != null && expectedChecksum.Length > 0)
            {
                var actualChecksum = await ComputeFileChecksumAsync(Path.Combine(modelPath, "pytorch_model.bin"));
                if (!actualChecksum.SequenceEqual(expectedChecksum))
                {
                    throw new Exception("Model checksum verification failed");
                }
            }

            return modelPath;
        }

        private string SanitizeModelId(string modelId)
        {
            return modelId.Replace("/", "_").Replace("\\", "_");
        }

        private async Task<byte[]> ComputeFileChecksumAsync(string filePath)
        {
            using var sha256 = SHA256.Create();
            await using var fs = new FileStream(filePath, FileMode.Open);
            return await sha256.ComputeHashAsync(fs);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                (_modelDownloader as IDisposable)?.Dispose();
                _disposed = true;
            }
        }
    }
}