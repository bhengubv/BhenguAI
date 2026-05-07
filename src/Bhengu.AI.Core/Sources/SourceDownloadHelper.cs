using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Core.Sources
{
    /// <summary>
    /// Shared HTTP-streaming download routine used by IModelSource implementations.
    /// Handles resume (Range requests), progress reporting, and ETA estimation.
    /// </summary>
    internal static class SourceDownloadHelper
    {
        private const int BufferSize = 8192;
        private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(500);

        public static async Task DownloadWithProgressAsync(
            HttpClient client,
            string url,
            string localPath,
            IProgress<DownloadProgress>? progress,
            CancellationToken ct)
        {
            var fileName = Path.GetFileName(localPath);

            // Resume support: if a partial file exists, ask the server for the rest.
            long existingBytes = 0;
            if (File.Exists(localPath))
            {
                existingBytes = new FileInfo(localPath).Length;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existingBytes > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existingBytes, null);
            }

            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            // If the server doesn't honor the Range request (returns 200 OK with full body),
            // restart from scratch instead of corrupting the file.
            FileMode fileMode;
            long startBytes;
            if (existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent)
            {
                fileMode = FileMode.Append;
                startBytes = existingBytes;
            }
            else if (existingBytes > 0 && response.IsSuccessStatusCode)
            {
                fileMode = FileMode.Create;
                startBytes = 0;
            }
            else
            {
                response.EnsureSuccessStatusCode();
                fileMode = FileMode.Create;
                startBytes = 0;
            }

            response.EnsureSuccessStatusCode();

            // ContentLength reflects only the bytes we'll receive on this response.
            // Total expected size = bytes already on disk + bytes still to come.
            var remainingFromServer = response.Content.Headers.ContentLength ?? -1;
            long totalBytes = remainingFromServer > 0
                ? startBytes + remainingFromServer
                : -1;

            var bytesRead = startBytes;
            var buffer = new byte[BufferSize];

            var stopwatch = Stopwatch.StartNew();
            var lastUpdateTime = stopwatch.Elapsed;
            var lastBytesRead = bytesRead;

            await using var fileStream = new FileStream(localPath, fileMode, FileAccess.Write, FileShare.None);
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            while (true)
            {
                var read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                if (read == 0) break;

                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                bytesRead += read;

                if (stopwatch.Elapsed - lastUpdateTime > ProgressInterval || bytesRead == totalBytes)
                {
                    var timeElapsed = stopwatch.Elapsed - lastUpdateTime;
                    var bytesDiff = bytesRead - lastBytesRead;
                    var bytesPerSecond = timeElapsed.TotalSeconds > 0
                        ? bytesDiff / timeElapsed.TotalSeconds
                        : 0;

                    TimeSpan eta = TimeSpan.Zero;
                    if (totalBytes > 0 && bytesPerSecond > 0)
                    {
                        var remainingBytes = totalBytes - bytesRead;
                        if (remainingBytes > 0)
                        {
                            eta = TimeSpan.FromSeconds(remainingBytes / bytesPerSecond);
                        }
                    }

                    progress?.Report(new DownloadProgress
                    {
                        FileName = fileName,
                        BytesReceived = bytesRead,
                        TotalBytes = totalBytes,
                        BytesPerSecond = bytesPerSecond,
                        EstimatedTimeRemaining = eta,
                    });

                    lastUpdateTime = stopwatch.Elapsed;
                    lastBytesRead = bytesRead;
                }
            }

            stopwatch.Stop();
        }
    }
}
