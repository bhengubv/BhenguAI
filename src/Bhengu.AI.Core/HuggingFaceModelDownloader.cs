using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Core
{
    public class HuggingFaceModelDownloader : IModelDownloader, IDisposable
    {
        public class DownloadProgressReport
        {
            public string FileName { get; set; }
            public long BytesReceived { get; set; }
            public long TotalBytes { get; set; }
            public double BytesPerSecond { get; set; }
            public TimeSpan EstimatedTimeRemaining { get; set; }
        }

        public delegate void DownloadProgressHandler(DownloadProgressReport progress);
        public event DownloadProgressHandler ProgressChanged;

        private readonly HttpClient _httpClient;
        private bool _disposed = false;
        private const string HuggingFaceRawUrl = "https://huggingface.co/{0}/resolve/main/{1}";

        public HuggingFaceModelDownloader(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "BhenguAI");
            _httpClient.Timeout = TimeSpan.FromMinutes(30);
        }

        public async Task DownloadModelAsync(string modelId, string localPath, CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HuggingFaceModelDownloader));
            if (string.IsNullOrWhiteSpace(modelId)) throw new ArgumentNullException(nameof(modelId));

            Directory.CreateDirectory(localPath);

            try
            {
                // Phi-3-mini specific files (updated to correct filenames)
                var filesToDownload = new[]
                {
                    "config.json",
                    "model.safetensors.index.json",  // Index file for sharded model
                    "model-00001-of-00002.safetensors",  // First shard
                    "model-00002-of-00002.safetensors",  // Second shard
                    "tokenizer.json",
                    "tokenizer_config.json",
                    "special_tokens_map.json",
                    "generation_config.json"
                };

                foreach (var file in filesToDownload)
                {
                    await DownloadFileWithProgressAsync(modelId, file, localPath, ct);
                }
            }
            catch (Exception ex)
            {
                CleanupPartialDownload(localPath);
                throw new Exception($"Failed to download model {modelId}", ex);
            }
        }

        private async Task DownloadFileWithProgressAsync(string modelId, string fileName, string localPath, CancellationToken ct)
        {
            var fileUrl = string.Format(HuggingFaceRawUrl, modelId, fileName);
            var filePath = Path.Combine(localPath, fileName);

            using var response = await _httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var bytesRead = 0L;
            var buffer = new byte[8192];
            var isMoreToRead = true;

            var stopwatch = Stopwatch.StartNew();
            var lastUpdateTime = stopwatch.Elapsed;
            var lastBytesRead = 0L;

            await using var fileStream = new FileStream(filePath, FileMode.Create);
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);

            Console.WriteLine($"Downloading {fileName}...");

            while (isMoreToRead)
            {
                var read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (read == 0)
                {
                    isMoreToRead = false;
                    continue;
                }

                await fileStream.WriteAsync(buffer, 0, read, ct);
                bytesRead += read;

                // Calculate speed every 500ms
                if (stopwatch.Elapsed - lastUpdateTime > TimeSpan.FromMilliseconds(500) || bytesRead == totalBytes)
                {
                    var timeElapsed = stopwatch.Elapsed - lastUpdateTime;
                    var bytesDiff = bytesRead - lastBytesRead;
                    var bytesPerSecond = bytesDiff / timeElapsed.TotalSeconds;

                    TimeSpan? eta = null;
                    if (totalBytes > 0 && bytesPerSecond > 0)
                    {
                        var remainingBytes = totalBytes - bytesRead;
                        eta = TimeSpan.FromSeconds(remainingBytes / bytesPerSecond);
                    }

                    ProgressChanged?.Invoke(new DownloadProgressReport
                    {
                        FileName = fileName,
                        BytesReceived = bytesRead,
                        TotalBytes = totalBytes,
                        BytesPerSecond = bytesPerSecond,
                        EstimatedTimeRemaining = eta ?? TimeSpan.Zero
                    });

                    UpdateConsoleProgress(fileName, bytesRead, totalBytes, bytesPerSecond, eta);

                    lastUpdateTime = stopwatch.Elapsed;
                    lastBytesRead = bytesRead;
                }
            }

            Console.WriteLine(); // New line after progress
            stopwatch.Stop();
        }

        private void UpdateConsoleProgress(string fileName, long bytesRead, long totalBytes, double bytesPerSecond, TimeSpan? eta)
        {
            Console.CursorLeft = 0;

            // File name (truncated if too long)
            var displayName = fileName.Length > 20 ? fileName.Substring(0, 17) + "..." : fileName;
            Console.Write($"{displayName.PadRight(20)} ");

            // Progress bar
            if (totalBytes > 0)
            {
                var progress = (double)bytesRead / totalBytes;
                var progressBar = new string('■', (int)(progress * 20));
                var progressBarEmpty = new string(' ', 20 - (int)(progress * 20));
                Console.Write($"[{progressBar}{progressBarEmpty}] ");
                Console.Write($"{progress:P0} ");
            }
            else
            {
                Console.Write($"[{new string(' ', 20)}] --- ");
            }

            // Size information
            Console.Write($"{FormatBytes(bytesRead)}");
            if (totalBytes > 0) Console.Write($"/{FormatBytes(totalBytes)} ");
            else Console.Write(" ");

            // Speed and ETA
            Console.Write($"@ {FormatBytes((long)bytesPerSecond)}/s");
            if (eta.HasValue && eta.Value.TotalSeconds > 0)
            {
                Console.Write($" (ETA: {FormatTimeSpan(eta.Value)})");
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double len = bytes;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return order == 0 ? $"{bytes}B" : $"{len:0.0}{sizes[order]}";
        }

        private string FormatTimeSpan(TimeSpan span)
        {
            if (span.TotalHours >= 1)
                return $"{span.TotalHours:0.0}h";
            if (span.TotalMinutes >= 1)
                return $"{span.TotalMinutes:0.0}m";
            return $"{span.TotalSeconds:0}s";
        }

        private void CleanupPartialDownload(string localPath)
        {
            try
            {
                if (Directory.Exists(localPath))
                {
                    Directory.Delete(localPath, true);
                }
            }
            catch { /* Ignore cleanup errors */ }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }
}