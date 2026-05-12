using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Circle.AI.Core
{
    /// <summary>
    /// Source-agnostic model downloader. Walks a list of <see cref="IModelSource"/>
    /// instances in order, falling through on failure so that one supplier going dark
    /// (sanctions, regional blocks, outages) does not break model bootstrap.
    /// </summary>
    public sealed class ModelDownloader : IModelDownloader, IDisposable
    {
        /// <summary>
        /// Backwards-compatible progress shape. Mirrors <see cref="DownloadProgress"/>
        /// but kept as a class+event so existing consumers of HuggingFaceModelDownloader
        /// can keep their wiring.
        /// </summary>
        public sealed class DownloadProgressReport
        {
            public string FileName { get; set; } = "";
            public long BytesReceived { get; set; }
            public long TotalBytes { get; set; }
            public double BytesPerSecond { get; set; }
            public TimeSpan EstimatedTimeRemaining { get; set; }
        }

        public delegate void DownloadProgressHandler(DownloadProgressReport progress);
        public event DownloadProgressHandler? ProgressChanged;

        private const string RegistryResourceName = "Circle.AI.Core.registry.json";

        private readonly IReadOnlyList<IModelSource> _sources;
        private readonly bool _ownsSources;
        private readonly Lazy<IReadOnlyDictionary<string, ModelEntry>> _registry;
        private bool _disposed;

        public ModelDownloader(IReadOnlyList<IModelSource> sources, bool ownsSources = false)
        {
            if (sources is null) throw new ArgumentNullException(nameof(sources));
            if (sources.Count == 0)
                throw new ArgumentException("At least one model source is required", nameof(sources));

            _sources = sources;
            _ownsSources = ownsSources;
            _registry = new Lazy<IReadOnlyDictionary<string, ModelEntry>>(LoadEmbeddedRegistry);
        }

        /// <inheritdoc />
        public async Task DownloadModelAsync(string modelId, string localPath, CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ModelDownloader));
            if (string.IsNullOrWhiteSpace(modelId)) throw new ArgumentNullException(nameof(modelId));
            if (string.IsNullOrWhiteSpace(localPath)) throw new ArgumentNullException(nameof(localPath));

            if (!_registry.Value.TryGetValue(modelId, out var entry))
            {
                throw new KeyNotFoundException(
                    $"Model '{modelId}' is not in the embedded registry. Known models: " +
                    string.Join(", ", _registry.Value.Keys));
            }

            Directory.CreateDirectory(localPath);
            var targetFile = Path.Combine(localPath, entry.FileName);

            var candidates = BuildCandidateList(entry);
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Model '{modelId}' has no PrimaryUrl or FallbackUrl configured.");
            }

            var bridge = new Progress<DownloadProgress>(p =>
                ProgressChanged?.Invoke(new DownloadProgressReport
                {
                    FileName = p.FileName,
                    BytesReceived = p.BytesReceived,
                    TotalBytes = p.TotalBytes,
                    BytesPerSecond = p.BytesPerSecond,
                    EstimatedTimeRemaining = p.EstimatedTimeRemaining,
                }));

            try
            {
                var winner = await DownloadFromCandidatesAsync(candidates, targetFile, bridge, ct).ConfigureAwait(false);
                Console.WriteLine($"[ModelDownloader] '{modelId}' downloaded via {winner}.");
            }
            catch
            {
                CleanupPartialFile(targetFile);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<string> DownloadFromCandidatesAsync(
            IReadOnlyList<string> candidateUrls,
            string localFilePath,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ModelDownloader));
            if (candidateUrls is null) throw new ArgumentNullException(nameof(candidateUrls));
            if (candidateUrls.Count == 0)
                throw new ArgumentException("At least one candidate URL is required", nameof(candidateUrls));
            if (string.IsNullOrWhiteSpace(localFilePath)) throw new ArgumentNullException(nameof(localFilePath));

            var dir = Path.GetDirectoryName(localFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var failures = new List<string>();

            foreach (var url in candidateUrls)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(url)) continue;

                var source = MatchSource(url);
                if (source is null)
                {
                    Console.WriteLine($"[ModelDownloader] Warning: no registered source matched URL '{url}' — skipping. Add a source whose Name matches the hostname, or extend MatchSource.");
                    failures.Add($"(no registered source for '{url}')");
                    continue;
                }

                try
                {
                    Console.WriteLine($"[ModelDownloader] Trying {source.Name}: {url}");
                    await source.DownloadAsync(url, localFilePath, progress, ct).ConfigureAwait(false);
                    Console.WriteLine($"[ModelDownloader] {source.Name} succeeded.");
                    return source.Name;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failures.Add($"{source.Name}: {ex.Message}");
                    Console.WriteLine($"[ModelDownloader] {source.Name} failed: {ex.Message}. Falling through.");
                    // Drop the partial so the next source can start clean.
                    CleanupPartialFile(localFilePath);
                }
            }

            throw new InvalidOperationException(
                "All model sources failed:\n  " + string.Join("\n  ", failures));
        }

        private IModelSource? MatchSource(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            var host = uri.Host;

            // Heuristic match by source Name, then by host substring.
            foreach (var s in _sources)
            {
                if (host.Contains(s.Name, StringComparison.OrdinalIgnoreCase))
                    return s;
            }

            // Specific known hosts.
            if (host.Contains("modelscope", StringComparison.OrdinalIgnoreCase))
                return _sources.FirstOrDefault(s => s.Name.Equals("ModelScope", StringComparison.OrdinalIgnoreCase));

            if (host.Contains("huggingface", StringComparison.OrdinalIgnoreCase))
                return _sources.FirstOrDefault(s => s.Name.Equals("HuggingFace", StringComparison.OrdinalIgnoreCase));

            return null;
        }

        private static IReadOnlyList<string> BuildCandidateList(ModelEntry entry)
        {
            var list = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(entry.PrimaryUrl)) list.Add(entry.PrimaryUrl);
            if (!string.IsNullOrWhiteSpace(entry.FallbackUrl)) list.Add(entry.FallbackUrl);
            return list;
        }

        private static IReadOnlyDictionary<string, ModelEntry> LoadEmbeddedRegistry()
        {
            var assembly = typeof(ModelDownloader).Assembly;
            using var stream = assembly.GetManifestResourceStream(RegistryResourceName);
            if (stream is null)
            {
                // Registry isn't embedded — fall back to a sibling registry.json next to the assembly.
                var assemblyDir = Path.GetDirectoryName(assembly.Location);
                var fallback = assemblyDir is null
                    ? null
                    : Path.Combine(assemblyDir, "registry.json");

                if (fallback is null || !File.Exists(fallback))
                {
                    return new Dictionary<string, ModelEntry>(StringComparer.OrdinalIgnoreCase);
                }

                using var fs = File.OpenRead(fallback);
                return JsonSerializer.Deserialize<Dictionary<string, ModelEntry>>(fs, JsonOpts())
                       ?? new Dictionary<string, ModelEntry>(StringComparer.OrdinalIgnoreCase);
            }

            return JsonSerializer.Deserialize<Dictionary<string, ModelEntry>>(stream, JsonOpts())
                   ?? new Dictionary<string, ModelEntry>(StringComparer.OrdinalIgnoreCase);
        }

        private static JsonSerializerOptions JsonOpts() => new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        private static void CleanupPartialFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Best effort.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_ownsSources)
            {
                foreach (var s in _sources)
                {
                    (s as IDisposable)?.Dispose();
                }
            }
            _disposed = true;
        }

        private sealed record ModelEntry
        {
            [JsonPropertyName("FileName")] public string FileName { get; init; } = "";
            [JsonPropertyName("PrimaryUrl")] public string? PrimaryUrl { get; init; }
            [JsonPropertyName("FallbackUrl")] public string? FallbackUrl { get; init; }
            [JsonPropertyName("Checksum")] public string? Checksum { get; init; }
            [JsonPropertyName("SizeBytes")] public long SizeBytes { get; init; }
            [JsonPropertyName("Version")] public string? Version { get; init; }
            [JsonPropertyName("Architecture")] public string? Architecture { get; init; }
            [JsonPropertyName("QuantizationType")] public string? QuantizationType { get; init; }
        }
    }
}
