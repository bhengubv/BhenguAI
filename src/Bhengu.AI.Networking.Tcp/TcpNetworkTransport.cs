using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Bhengu.AI.Networking;

namespace Bhengu.AI.Networking.Tcp;

/// <summary>
/// <see cref="INetworkTransport"/> over raw TCP.
/// Acts as client when <paramref name="remoteEndpoint"/> is set;
/// acts as server listener when only <paramref name="listenPort"/> is set.
/// </summary>
public sealed class TcpNetworkTransport : INetworkTransport, IAsyncDisposable
{
    private readonly IPEndPoint? _remote;
    private readonly int? _listenPort;
    private TcpClient? _client;
    private TcpListener? _listener;
    private NetworkStream? _stream;
    private readonly Channel<NetworkPayload> _inbound = Channel.CreateUnbounded<NetworkPayload>();

    public TransportKind Kind    => TransportKind.Tcp;
    public bool IsAvailable      => _client?.Connected ?? false;

    public TcpNetworkTransport(IPEndPoint? remoteEndpoint = null, int? listenPort = null)
    {
        _remote     = remoteEndpoint;
        _listenPort = listenPort;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_remote is not null)
        {
            _client = new TcpClient();
            await _client.ConnectAsync(_remote, ct).ConfigureAwait(false);
            _stream = _client.GetStream();
            _ = PumpAsync(ct);
        }
        else if (_listenPort.HasValue)
        {
            _listener = new TcpListener(IPAddress.Any, _listenPort.Value);
            _listener.Start();
        }
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _stream?.Close();
        _client?.Close();
        _listener?.Stop();
        _inbound.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public async Task SendAsync(NetworkPayload payload, CancellationToken ct = default)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected.");
        var data  = payload.Data.ToArray();
        var len   = BitConverter.GetBytes(data.Length);
        await _stream.WriteAsync(len, ct).ConfigureAwait(false);
        await _stream.WriteAsync(data, ct).ConfigureAwait(false);
    }

    public IAsyncEnumerable<NetworkPayload> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
        => _inbound.Reader.ReadAllAsync(ct);

    private async Task PumpAsync(CancellationToken ct)
    {
        while (_stream is not null && !ct.IsCancellationRequested)
        {
            try
            {
                var lenBuf = new byte[4];
                await _stream.ReadExactlyAsync(lenBuf, ct).ConfigureAwait(false);
                var len  = BitConverter.ToInt32(lenBuf);
                var data = new byte[len];
                await _stream.ReadExactlyAsync(data, ct).ConfigureAwait(false);
                await _inbound.Writer.WriteAsync(NetworkPayload.Create(data), ct).ConfigureAwait(false);
            }
            catch { break; }
        }
        _inbound.Writer.TryComplete();
    }

    public ValueTask DisposeAsync()
    {
        StopAsync();
        return ValueTask.CompletedTask;
    }
}
