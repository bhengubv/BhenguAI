using System.Security;
using System.Security.Cryptography;
using System.Text.Json;

namespace Bhengu.AI.Core.Models
{
    public sealed class ModelRegistryService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _registryPath;
        private ModelRegistry _embeddedRegistry;
        private ModelRegistry _remoteRegistry;
        private bool _disposed;

        public ModelRegistryService(string registryUrl = null)
        {
            _httpClient = new HttpClient();
            _registryPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BhenguAI",
                "Models",
                "remote_registry.json");

            // Load embedded registry (fallback)
            var assembly = typeof(ModelRegistryService).Assembly;
            using var stream = assembly.GetManifestResourceStream("Bhengu.AI.Core.Models.embedded_registry.json");
            _embeddedRegistry = JsonSerializer.Deserialize<ModelRegistry>(stream);
        }

        public async Task CheckForUpdatesAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync(_embeddedRegistry.RegistryUrl);
                response.EnsureSuccessStatusCode();

                var remoteJson = await response.Content.ReadAsStringAsync();
                if (!VerifySignature(remoteJson))
                    throw new SecurityException("Invalid registry signature");

                _remoteRegistry = JsonSerializer.Deserialize<ModelRegistry>(remoteJson);
                await File.WriteAllTextAsync(_registryPath, remoteJson);
            }
            catch
            {
                // Fallback to embedded registry
                _remoteRegistry = _embeddedRegistry;
            }
        }

        public ModelEntry GetLatestModel(string modelName)
        {
            var registry = _remoteRegistry ?? _embeddedRegistry;
            return registry.Models.FirstOrDefault(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase));
        }

        private bool VerifySignature(string json)
        {
            // Implement ECDSA/PGP verification here
            return true; // Simplified for example
        }

        public void Dispose()
        {
            if (_disposed) return;
            _httpClient.Dispose();
            _disposed = true;
        }
    }

    public record ModelRegistry(
        string RegistryUrl,
        DateTime LastUpdated,
        List<ModelEntry> Models);

    public record ModelEntry(
        string Name,
        string Version,
        string Quantization,
        string Url,
        string Checksum);
}