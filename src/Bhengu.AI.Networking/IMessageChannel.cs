using System.Runtime.CompilerServices;

namespace Bhengu.AI.Networking;

/// <summary>Typed message delivery over any transport.</summary>
public interface IMessageChannel
{
    Task SendAsync<T>(string destinationId, T message, CancellationToken ct = default)
        where T : class;

    IAsyncEnumerable<T> ReceiveAsync<T>(
        [EnumeratorCancellation] CancellationToken ct = default)
        where T : class;
}
