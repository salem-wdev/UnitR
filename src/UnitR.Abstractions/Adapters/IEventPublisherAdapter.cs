namespace UnitR.Abstractions.Adapters;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Defines a decoupled abstraction adapter for dispatching domain notifications and events.
/// Decouples the transactional unit of work engine from concrete messaging providers (e.g., MediatR, MassTransit).
/// </summary>
public interface IEventPublisherAdapter
{
    /// <summary>
    /// Publishes a single domain notification or event asynchronously to registered handlers.
    /// </summary>
    /// <param name="notification">The domain notification instance to publish.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous publication operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="notification"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is canceled.</exception>
    Task PublishAsync(
        object notification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a collection of domain notifications or events asynchronously.
    /// Allows the underlying adapter to optimize batch execution (e.g., sequential vs concurrent).
    /// </summary>
    /// <param name="notifications">The sequence of domain notifications to publish.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous publication operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="notifications"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is canceled.</exception>
    Task PublishBatchAsync(
        IEnumerable<object> notifications,
        CancellationToken cancellationToken = default);
}