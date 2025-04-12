using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Bhengu.AI.Core
{
    public sealed class LocalModelLoader : IModelLoader
    {
        private const string RegistryResourceName = "Bhengu.AI.Core.Models.registry.json";
        private readonly HttpClient _httpClient = new();
        private readonly string _modelDir;
        private readonly Dictionary<string, ModelInfo> _modelRegistry;
        private bool _disposed;

        public LocalModelLoader(string modelDirectory = null)
        {
            _modelDir = modelDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BhenguAI",
                "Models");

            Directory.CreateDirectory(_modelDir);
            _modelRegistry = LoadEmbeddedRegistry();
        }

        public async Task<string> DownloadModelAsync(string modelName, IProgress<float>? progress = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LocalModelLoader));
            if (!_modelRegistry.TryGetValue(modelName, out var modelInfo))
                throw new ArgumentException($"Model {modelName} not supported");

            string localPath = Path.Combine(_modelDir, modelInfo.FileName);

            if (File.Exists(localPath))
            {
                if (VerifyChecksum(localPath, modelInfo.Checksum))
                    return localPath;
                File.Delete(localPath);
            }

            await DownloadFileAsync(modelInfo.Url, localPath, progress ?? new Progress<float>());

            if (!VerifyChecksum(localPath, modelInfo.Checksum))
                throw new InvalidDataException("Downloaded model failed verification");

            return localPath;
        }

        private async Task DownloadFileAsync(string url, string outputPath, IProgress<float> progress)
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var fs = new FileStream(outputPath, FileMode.Create);
            await response.Content.CopyToAsync(fs);
        }

        public string GetModelPath(string modelName)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LocalModelLoader));
            if (!_modelRegistry.TryGetValue(modelName, out var modelInfo))
                throw new FileNotFoundException($"Model {modelName} not found");

            return Path.Combine(_modelDir, modelInfo.FileName);
        }

        public bool ModelExists(string modelName)
        {
            try
            {
                var path = GetModelPath(modelName);
                return File.Exists(path) && VerifyChecksum(path, _modelRegistry[modelName].Checksum);
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> CheckForCriticalUpdateAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(
                    "https://raw.githubusercontent.com/BhenguAI/models/main/versions.txt");
                return response.Contains("[CRITICAL]");
            }
            catch
            {
                return false;
            }
        }

        private Dictionary<string, ModelInfo> LoadEmbeddedRegistry()
        {
            var assembly = typeof(LocalModelLoader).Assembly;
            using var stream = assembly.GetManifestResourceStream(RegistryResourceName)
                ?? throw new FileNotFoundException("Embedded registry not found");

            return JsonSerializer.Deserialize<Dictionary<string, ModelInfo>>(stream)
                ?? throw new InvalidDataException("Invalid registry format");
        }

        private bool VerifyChecksum(string filePath, string expectedChecksum)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = sha256.ComputeHash(stream);
            var actualChecksum = "sha256:" + BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            return actualChecksum == expectedChecksum;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _httpClient.Dispose();
            _disposed = true;
        }

        private record ModelInfo(
            string FileName,
            string Url,
            string Checksum);
    }
}