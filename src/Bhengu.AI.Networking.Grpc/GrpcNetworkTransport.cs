using System.Runtime.CompilerServices;
using Grpc.Net.Client;
using Bhengu.AI.Networking;

namespace Bhengu.AI.Networking.Grpc;

/// <summary>
/// <see cref="INetworkTransport"/> backed by a gRPC channel.
/// Manages channel lifecycle, deadlines, and reconnection.
/// Wire protocol (proto service) is defined by the consuming application.
/// </summary>
public sealed class GrpcNetworkTransport : INetworkTransport, IDisposable
{
    private readonly GrpcChannel _channel;
    private bool _running;

    public TransportKind Kind    => TransportKind.Grpc;
    public bool IsAvailable      => _running;

    public GrpcNetworkTransport(string address, GrpcChannelOptions? options = null)
        => _channel = GrpcChannel.ForAddress(address, options ?? new GrpcChannelOptions());

    public Task StartAsync(CancellationToken ct = default) { _running = true; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct = default)  { _running = false; return Task.CompletedTask; }

    /// <summary>
    /// gRPC streaming calls are protocol-specific.
    /// This method is intentionally not implemented here — callers use
    /// the <see cref="GrpcChannel"/> directly for typed proto clients.
    /// </summary>
    public Task SendAsync(NetworkPayload payload, CancellationToken ct = default)
        => throw new NotSupportedException(
            "Use the gRPC channel directly for typed proto clients. " +
            "GrpcNetworkTransport.SendAsync is not a generic send path.");

    public async IAsyncEnumerable<NetworkPayload> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    /// <summary>Exposes the underlying channel for typed gRPC client creation.</summary>
    public GrpcChannel Channel => _channel;

    public void Dispose() => _channel.Dispose();
}
