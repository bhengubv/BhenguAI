// IAIEndpoint.cs
//
// Transport-agnostic surface for exposing an IAIService. Implementations
// include in-process (direct call), HTTP loopback, and named pipes / socket
// IPC variants. Each endpoint owns whatever listener it needs and is
// responsible for marshalling cancellation through to the underlying service.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Hosting;

/// <summary>
/// Transport-agnostic endpoint that exposes a <see cref="IAIService"/>.
/// </summary>
public interface IAIEndpoint : IAsyncDisposable
{
    /// <summary>
    /// Begins serving requests against the supplied <paramref name="service"/>.
    /// Idempotent — calling twice is a no-op after the first success.
    /// </summary>
    Task StartAsync(IAIService service, CancellationToken ct = default);

    /// <summary>
    /// Stops accepting new requests and waits for in-flight ones to drain.
    /// </summary>
    Task StopAsync(CancellationToken ct = default);
}
