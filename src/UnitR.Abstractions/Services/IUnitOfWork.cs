namespace UnitR.Abstractions.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using UnitR.Abstractions.Events;

/// <summary>
/// Defines the transactional unit-of-work orchestrator contract.
/// Coordinates execution scopes, transaction boundaries, and the two-phase dispatching of pre-commit and post-commit domain events.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Initiates a transactional execution scope, enlisting in an ambient physical transaction or establishing a new root boundary.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous initialization operation.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the instance has already been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when enlisting into a failed or rolled-back transaction.</exception>
    Task BeginAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the current execution scope, dispatching pre-commit events, persisting database changes,
    /// committing the underlying physical transaction at the root level, and triggering post-commit notifications.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <param name="explicitEvents">Optional domain events supplied explicitly to be batched and dispatched across the commit pipeline.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous commit operation.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the instance has already been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when invoked prior to <see cref="BeginAsync"/> or on an already rolled-back scope.</exception>
    Task CommitAsync(
        CancellationToken cancellationToken = default,
        params IUnitREvent[] explicitEvents);
}