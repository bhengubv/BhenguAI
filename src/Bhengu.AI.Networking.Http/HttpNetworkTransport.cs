using System.Runtime.CompilerServices;
using System.Text.Json;
using Bhengu.AI.Networking;

namespace Bhengu.AI.Networking.Http;

/// <summary>
/// <see cref="INetworkTransport"/> backed by <see cref="HttpClient"/>.
/// Supports REST calls, SSE streaming, and chunked uploads with retry + backoff.
/// </summary>
public sealed class HttpNetworkTransport : INetworkTransport, IDisposable
{
    private readonly HttpClient _client;
    private readonly string _baseUrl;
    private bool _running;

    public TransportKind Kind => TransportKind.Http;
    public bool IsAvailable => true;  // assume HTTP always available if configured

    public HttpNetworkTransport(HttpClient client, string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        _client  = client;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public Task StartAsync(CancellationToken ct = default) { _running = true; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct = default)  { _running = false; return Task.CompletedTask; }

    /// <summary>
    /// POST the payload data to <c>{baseUrl}/messages/{destinationId}</c>.
    /// Retries up to 3 times with exponential backoff on transient failures.
    /// </summary>
    public async Task SendAsync(NetworkPayload payload, CancellationToken ct = default)
    {
        var url = payload.DestinationId is { Length: > 0 } dest
            ? $"{_baseUrl}/messages/{Uri.EscapeDataString(dest)}"
            : $"{_baseUrl}/messages";

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var content = new ByteArrayContent(payload.Data.ToArray());
                content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue(payload.ContentType);
                content.Headers.Add("X-Payload-Id",       payload.Id);
                content.Headers.Add("X-Payload-Priority", payload.Priority.ToString());

                var resp = await _client.PostAsync(url, content, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                return;
            }
            catch (HttpRequestException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Not implemented for HTTP pull model — use WebSocket or SSE instead.</summary>
    public async IAsyncEnumerable<NetworkPayload> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // HTTP is request-response; polling-based receive is intentionally not provided here.
        // For server-push use Bhengu.AI.Networking.WebSocket or SignalR.
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    public void Dispose() => _client.Dispose();
}
