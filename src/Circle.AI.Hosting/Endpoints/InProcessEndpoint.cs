// InProcessEndpoint.cs
//
// Trivial endpoint for callers that live in the same process as the
// butler service (e.g. a keyboard IME hosting Butler directly). It just
// holds the service reference behind a public accessor — there is no
// transport at all.

using System.Threading;
using System.Threading.Tasks;

namespace Circle.AI.Hosting.Endpoints;

/// <summary>
/// In-process endpoint. No transport — just exposes the underlying
/// <see cref="IAIService"/> directly so callers can invoke it as a
/// regular .NET object. Use this when keyboard and Butler share a process.
/// </summary>
public sealed class InProcessEndpoint : IAIEndpoint
{
    private IAIService? _service;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// The wrapped service. <c>null</c> until <see cref="StartAsync"/> has run.
    /// In-process callers can read this directly.
    /// </summary>
    public IAIService? ServiceAccessor => _service;

    /// <inheritdoc />
    public Task StartAsync(IAIService service, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return Task.CompletedTask;

        _service = service ?? throw new ArgumentNullException(nameof(service));
        _started = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct = default)
    {
        _started = false;
        _service = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _service = null;
        _started = false;
        return ValueTask.CompletedTask;
    }
}
