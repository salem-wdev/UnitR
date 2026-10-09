namespace UnitR.MediatR;

using System;
using global::MediatR;
using UnitR.Abstractions.Events;

/// <summary>
/// Wraps a UnitR domain event inside a MediatR notification contract.
/// Allows the inner domain model to remain decoupled from MediatR abstractions.
/// </summary>
public sealed class MediatRNotificationWrapper : INotification
{
    /// <summary>
    /// Gets the underlying UnitR domain event payload.
    /// </summary>
    public IUnitREvent DomainEvent { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MediatRNotificationWrapper"/> class.
    /// </summary>
    /// <param name="domainEvent">The domain event instance to wrap.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="domainEvent"/> is null.</exception>
    public MediatRNotificationWrapper(IUnitREvent domainEvent)
    {
        DomainEvent = domainEvent ?? throw new ArgumentNullException(nameof(domainEvent));
    }
}