// CompanionSessionFactory.cs
//
// Creates CompanionSession instances with all optional services resolved
// from the DI container. Callers only need the factory — they never
// construct CompanionSession directly.

using Bhengu.AI.Hosting;
using Bhengu.AI.Identity;
using Bhengu.AI.Memory;
using Bhengu.AI.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace Bhengu.AI.Companion;

/// <summary>
/// Contract for creating per-identity, per-surface Companion sessions.
/// </summary>
public interface ICompanionSessionFactory
{
    /// <summary>
    /// Creates a new <see cref="ICompanionSession"/> for the given identity
    /// and interface surface. Resolves all available backing services from DI.
    /// </summary>
    Task<ICompanionSession> CreateAsync(
        string identityId,
        InterfaceKind @interface,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class CompanionSessionFactory : ICompanionSessionFactory
{
    private readonly IServiceProvider _services;
    private readonly IIdentityProvider? _identity;

    public CompanionSessionFactory(
        IServiceProvider services,
        IIdentityProvider? identity = null)
    {
        _services = services;
        _identity = identity;
    }

    public async Task<ICompanionSession> CreateAsync(
        string identityId,
        InterfaceKind @interface,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityId);

        // Try to resolve a rich display name from the identity store.
        string displayName     = identityId;
        string? preferredLang  = null;

        if (_identity is not null)
        {
            var resolved = await _identity.GetCurrentIdentityAsync(ct).ConfigureAwait(false);
            if (resolved is not null)
            {
                displayName    = resolved.DisplayName;
                preferredLang  = resolved.PreferredLanguage;
            }
        }

        return new CompanionSession(
            identityId:        identityId,
            displayName:       displayName,
            @interface:        @interface,
            preferredLanguage: preferredLang,
            ai:       _services.GetService<IAIService>(),
            episodic: _services.GetService<IEpisodicMemoryStore>(),
            persona:  _services.GetService<IPersonaStore>(),
            affect:   _services.GetService<IAffectStore>(),
            goals:    _services.GetService<IGoalStore>(),
            sync:     _services.GetService<IMemorySyncService>(),
            proactive:_services.GetService<IProactiveReasoningService>()
        );
    }
}
