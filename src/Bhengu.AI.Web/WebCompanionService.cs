using Bhengu.AI.Companion;
using Microsoft.Extensions.DependencyInjection;

namespace Bhengu.AI.Web;

/// <summary>
/// Scoped Blazor service that manages the lifecycle of a single
/// <see cref="ICompanionSession"/> per browser tab / SignalR circuit.
/// Register as scoped via <see cref="ServiceCollectionExtensions.AddCircleWebCompanion"/>.
/// </summary>
public sealed class WebCompanionService : IAsyncDisposable
{
    private readonly ICompanionSessionFactory _factory;
    private ICompanionSession? _session;

    public WebCompanionService(ICompanionSessionFactory factory)
        => _factory = factory;

    /// <summary>The active session, once <see cref="InitialiseAsync"/> has been called.</summary>
    public ICompanionSession Session => _session
        ?? throw new InvalidOperationException("Call InitialiseAsync first.");

    /// <summary>
    /// Initialises the Companion session for the given identity.
    /// Safe to call multiple times — only creates a session once.
    /// </summary>
    public async Task InitialiseAsync(
        string identityId, CancellationToken ct = default)
    {
        if (_session is not null) return;
        _session = await _factory.CreateAsync(identityId, InterfaceKind.Web, ct)
                                 .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
            await _session.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>DI extensions for <see cref="WebCompanionService"/>.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="WebCompanionService"/> as a scoped service.
    /// Also registers <see cref="ICompanionSessionFactory"/> if not already present.
    /// </summary>
    public static IServiceCollection AddCircleWebCompanion(
        this IServiceCollection services)
    {
        services.AddCircleCompanion();
        services.AddScoped<WebCompanionService>();
        return services;
    }
}
