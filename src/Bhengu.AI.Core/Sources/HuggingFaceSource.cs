using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Core.Sources
{
    /// <summary>
    /// IModelSource implementation backed by HuggingFace (huggingface.co).
    /// Used as a fallback when ModelScope is unreachable.
    /// </summary>
    public sealed class HuggingFaceSource : IModelSource, IDisposable
    {
        private const string HostName = "huggingface.co";
        private const string ProbePath = "https://huggingface.co/";

        private readonly HttpClient _httpClient;
        private readonly bool _ownsClient;
        private bool _disposed;

        public string Name => "HuggingFace";

        public HuggingFaceSource(HttpClient? httpClient = null)
        {
            if (httpClient is null)
            {
                _httpClient = new HttpClient();
                _ownsClient = true;
            }
            else
            {
                _httpClient = httpClient;
                _ownsClient = false;
            }

            if (!_httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("BhenguAI"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "BhenguAI");
            }
            _httpClient.Timeout = TimeSpan.FromMinutes(30);
        }

        public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
        {
            if (_disposed) return false;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, ProbePath);
                using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                return res.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task DownloadAsync(
            string url,
            string localPath,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HuggingFaceSource));
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentNullException(nameof(url));
            if (string.IsNullOrWhiteSpace(localPath)) throw new ArgumentNullException(nameof(localPath));

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !uri.Host.EndsWith(HostName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"URL host must be on {HostName} for {Name} source. Got: {url}", nameof(url));
            }

            var dir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await SourceDownloadHelper.DownloadWithProgressAsync(
                _httpClient, url, localPath, progress, ct).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_ownsClient) _httpClient.Dispose();
            _disposed = true;
        }
    }
}
