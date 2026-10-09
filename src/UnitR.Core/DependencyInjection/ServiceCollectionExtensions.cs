namespace Microsoft.Extensions.DependencyInjection;

using System;
using UnitR.Abstractions.Services;
using UnitR.Core.Engine;
using UnitR.Core.Options;

/// <summary>
/// Provides extension methods for registering core UnitR services and transaction orchestrators into the DI container.
/// </summary>
public static class UnitRCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core UnitR transactional orchestrator and configuration options.
    /// </summary>
    /// <param name="services">The service collection to register dependencies into.</param>
    /// <param name="configureOptions">An optional delegate to configure <see cref="UnitROptions"/>.</param>
    /// <returns>The same service collection for fluent method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddUnitR(
        this IServiceCollection services,
        Action<UnitROptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 1. Configure UnitROptions via the Options pattern
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.AddOptions<UnitROptions>();
        }

        // 2. Register the Unit of Work engine with a Scoped lifetime
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}