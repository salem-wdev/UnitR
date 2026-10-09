namespace UnitR.Abstractions.Adapters;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnitR.Abstractions.Events;

/// <summary>
/// Defines a decoupled abstraction adapter for dispatching domain notifications and events.
/// Decouples the transactional unit of work engine from concrete messaging providers (e.g., MediatR, MassTransit).
/// Enforces strong typing by restricting publication exclusively to UnitR-governed domain events.
/// </summary>
public interface IEventPublisherAdapter
{
    /// <summary>
    /// Publishes a single UnitR domain event asynchronously to registered handlers.
    /// </summary>
    /// <param name="domainEvent">The domain event instance implementing <see cref="IUnitREvent"/> to publish.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous publication operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="domainEvent"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is canceled.</exception>
    Task PublishAsync(
        IUnitREvent domainEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a collection of UnitR domain events asynchronously.
    /// Allows the underlying adapter to optimize batch execution (e.g., sequential vs concurrent).
    /// </summary>
    /// <param name="domainEvents">The sequence of domain events implementing <see cref="IUnitREvent"/> to publish.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous publication operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="domainEvents"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is canceled.</exception>
    Task PublishBatchAsync(
        IEnumerable<IUnitREvent> domainEvents,
        CancellationToken cancellationToken = default);
}