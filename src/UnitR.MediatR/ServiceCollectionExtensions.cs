namespace Microsoft.Extensions.DependencyInjection;

using System;
using UnitR.Abstractions.Adapters;
using UnitR.MediatR;

/// <summary>
/// Provides extension methods for registering the UnitR MediatR adapter into the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the MediatR adapter as the decoupled event publisher for the UnitR orchestrator.
    /// </summary>
    /// <param name="services">The service collection to register dependencies into.</param>
    /// <returns>The same service collection for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddUnitRMediatR(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IEventPublisherAdapter, MediatREventPublisherAdapter>();

        return services;
    }
}