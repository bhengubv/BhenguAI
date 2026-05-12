// ServiceCollectionExtensions.cs

using Microsoft.Extensions.DependencyInjection;

namespace Circle.AI.Companion;

/// <summary>
/// DI registration helpers for Circle.AI.Companion.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICompanionSessionFactory"/> as a singleton.
    /// All companion backing services (AI, memory, identity, sync, proactive)
    /// must be registered separately — Companion resolves them optionally.
    /// </summary>
    public static IServiceCollection AddCircleCompanion(
        this IServiceCollection services)
    {
        services.AddSingleton<ICompanionSessionFactory, CompanionSessionFactory>();
        return services;
    }
}
