namespace UnitR.MediatR;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using global::MediatR;
using UnitR.Abstractions.Adapters;
using UnitR.Abstractions.Events;

/// <summary>
/// Adapts the UnitR event publishing pipeline to dispatch notifications using the MediatR mediator engine.
/// Wraps native domain events into MediatR-compliant notification wrappers before publishing.
/// </summary>
public sealed class MediatREventPublisherAdapter : IEventPublisherAdapter
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediatREventPublisherAdapter"/> class.
    /// </summary>
    /// <param name="mediator">The MediatR mediator instance used to dispatch notifications.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mediator"/> is <see langword="null"/>.</exception>
    public MediatREventPublisherAdapter(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    /// <inheritdoc />
    public Task PublishAsync(
        IUnitREvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var notification = new MediatRNotificationWrapper(domainEvent);
        return _mediator.Publish(notification, cancellationToken);
    }

    /// <inheritdoc />
    public async Task PublishBatchAsync(
        IEnumerable<IUnitREvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        foreach (var domainEvent in domainEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await PublishAsync(domainEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}