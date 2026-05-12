using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Circle.AI.Core.Sources
{
    /// <summary>
    /// IModelSource implementation backed by ModelScope (modelscope.cn, Alibaba).
    /// Treated as the primary source for sanctions resilience.
    /// </summary>
    public sealed class ModelScopeSource : IModelSource, IDisposable
    {
        private const string HostName = "modelscope.cn";
        private const string ProbePath = "https://modelscope.cn/";

        private readonly HttpClient _httpClient;
        private readonly bool _ownsClient;
        private bool _disposed;

        public string Name => "ModelScope";

        public ModelScopeSource(HttpClient? httpClient = null)
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
            if (_disposed) throw new ObjectDisposedException(nameof(ModelScopeSource));
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
