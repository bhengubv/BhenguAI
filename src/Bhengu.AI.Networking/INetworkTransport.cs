using System.Runtime.CompilerServices;

namespace Bhengu.AI.Networking;

/// <summary>Unified send/receive abstraction for a single transport kind.</summary>
public interface INetworkTransport
{
    TransportKind Kind { get; }
    bool IsAvailable { get; }

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);

    Task SendAsync(NetworkPayload payload, CancellationToken ct = default);

    IAsyncEnumerable<NetworkPayload> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken ct = default);
}
