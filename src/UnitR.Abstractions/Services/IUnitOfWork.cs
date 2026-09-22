namespace UnitR.Abstractions.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;

/// <summary>
/// Defines the contract for orchestrating transactions and dispatching domain events.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Executes a transactional operation asynchronously and coordinates pre/post-commit events.
    /// </summary>
    /// <param name="action">The business workload to execute within the transaction boundary.</param>
    /// <param name="cancellationToken">Propagates notification that operations should be canceled.</param>
    /// <param name="explicitEvents">Optional explicit domain events to associate with this transaction.</param>
    Task ExecuteAsync(
        Func<Task> action,
        CancellationToken cancellationToken = default,
        params INotification[] explicitEvents);

    /// <summary>
    /// Executes a transactional operation asynchronously that returns a result and coordinates pre/post-commit events.
    /// </summary>
    /// <typeparam name="TResult">The type of the result returned by the operation.</typeparam>
    /// <param name="action">The business workload to execute within the transaction boundary.</param>
    /// <param name="cancellationToken">Propagates notification that operations should be canceled.</param>
    /// <param name="explicitEvents">Optional explicit domain events to associate with this transaction.</param>
    Task<TResult> ExecuteAsync<TResult>(
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default,
        params INotification[] explicitEvents);
}