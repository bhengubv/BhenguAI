using System.Security;
using System.Security.Cryptography;
using System.Text.Json;

namespace Bhengu.AI.Core.Models
{
    public sealed class ModelRegistryService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _registryPath;
        private ModelRegistry? _embeddedRegistry;
        private ModelRegistry? _remoteRegistry;
        private bool _disposed;

        public ModelRegistryService(string? registryUrl = null)
        {
            _httpClient = new HttpClient();
            _registryPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BhenguAI",
                "Models",
                "remote_registry.json");

            // Load embedded registry (fallback). Stream may be null if the resource is not embedded.
            var assembly = typeof(ModelRegistryService).Assembly;
            using var stream = assembly.GetManifestResourceStream("Bhengu.AI.Core.Models.embedded_registry.json");
            if (stream is not null)
            {
                _embeddedRegistry = JsonSerializer.Deserialize<ModelRegistry>(stream);
            }
        }

        public async Task CheckForUpdatesAsync()
        {
            try
            {
                var registryUrl = _embeddedRegistry?.RegistryUrl;
                if (string.IsNullOrWhiteSpace(registryUrl))
                {
                    _remoteRegistry = _embeddedRegistry;
                    return;
                }

                var response = await _httpClient.GetAsync(registryUrl);
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

        public ModelEntry? GetLatestModel(string modelName)
        {
            var registry = _remoteRegistry ?? _embeddedRegistry;
            return registry?.Models.FirstOrDefault(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool VerifySignature(string json)
        {
            // SECURITY: Signature verification infrastructure is not yet in place.
            // Throwing here causes CheckForUpdatesAsync's catch block to fall back
            // to the embedded registry, ensuring that no unsigned remote payload
            // (including one from a MITM or a compromised server) can ever be
            // deserialised and used as a source of model URLs.
            //
            // TODO: Replace with ECDSA / Ed25519 verification once the signing key
            //       and registry-signing workflow are established.  Until then remote
            //       registry updates are intentionally blocked.
            throw new NotSupportedException(
                "Remote registry signature verification is not yet implemented. " +
                "Remote updates are blocked until cryptographic signing is in place.");
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
