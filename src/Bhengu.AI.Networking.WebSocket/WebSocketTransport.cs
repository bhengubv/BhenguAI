using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Bhengu.AI.Networking;

namespace Bhengu.AI.Networking.WebSocket;

/// <summary>Full-duplex <see cref="INetworkTransport"/> backed by <see cref="ClientWebSocket"/>.</summary>
public sealed class WebSocketTransport : INetworkTransport, IAsyncDisposable
{
    private readonly Uri _endpoint;
    private ClientWebSocket? _ws;
    private readonly Channel<NetworkPayload> _inbound =
        Channel.CreateUnbounded<NetworkPayload>(new UnboundedChannelOptions { SingleReader = false });

    public TransportKind Kind    => TransportKind.WebSocket;
    public bool IsAvailable      => _ws?.State == WebSocketState.Open;

    public WebSocketTransport(string endpoint)
        => _endpoint = new Uri(endpoint);

    public async Task StartAsync(CancellationToken ct = default)
    {
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(_endpoint, ct).ConfigureAwait(false);
        _ = PumpAsync(ct);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_ws is not null)
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "stop", ct).ConfigureAwait(false);
        _inbound.Writer.TryComplete();
    }

    public async Task SendAsync(NetworkPayload payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(_ws);
        await _ws.SendAsync(
            payload.Data,
            System.Net.WebSockets.WebSocketMessageType.Binary,
            endOfMessage: true, ct).ConfigureAwait(false);
    }

    public IAsyncEnumerable<NetworkPayload> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
        => _inbound.Reader.ReadAllAsync(ct);

    private async Task PumpAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        while (_ws is not null && !ct.IsCancellationRequested)
        {
            try
            {
                var result = await _ws
                    .ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    break;
                var data = buffer.AsMemory(0, result.Count).ToArray();
                await _inbound.Writer.WriteAsync(
                    NetworkPayload.Create(data), ct).ConfigureAwait(false);
            }
            catch (WebSocketException) { break; }
            catch (OperationCanceledException) { break; }
        }
        _inbound.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        if (_ws is not null)
        {
            _ws.Dispose();
            _ws = null;
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
