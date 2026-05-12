using System.Runtime.CompilerServices;

namespace Circle.AI.Networking;

/// <summary>Observes connectivity state and emits changes.</summary>
public interface IConnectivityMonitor
{
    ConnectivityState CurrentState { get; }
    NetworkContext GetSnapshot();

    IAsyncEnumerable<NetworkContext> WatchAsync(
        [EnumeratorCancellation] CancellationToken ct = default);
}
