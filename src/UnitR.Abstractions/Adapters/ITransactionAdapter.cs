namespace UnitR.Abstractions.Adapters;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;

/// <summary>
/// Defines the persistence-agnostic abstraction for controlling database transaction lifecycles
/// and collecting encapsulated domain events from tracked entities.
/// </summary>
public interface ITransactionAdapter
{
    /// <summary>
    /// Gets a value indicating whether an ambient transaction boundary is actively running.
    /// </summary>
    bool HasActiveTransaction { get; }

    /// <summary>
    /// Initiates a new physical database transaction asynchronously.
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists state changes and commits the active physical transaction asynchronously.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Rolls back the active physical transaction asynchronously, reverting pending changes.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Extracts and clears encapsulated domain events from tracked aggregates within the persistence boundary.
    /// </summary>
    Task<IReadOnlyList<INotification>> HarvestDomainEventsAsync(CancellationToken cancellationToken);
}