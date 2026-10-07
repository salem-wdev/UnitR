namespace UnitR.Abstractions.Adapters;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Defines the persistence-agnostic abstraction for managing underlying database transaction lifecycles.
/// Concrete adapters (such as EF Core or ADO.NET) implement this contract to execute physical transaction boundaries.
/// </summary>
public interface ITransactionAdapter : IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether an active physical database transaction is currently running.
    /// </summary>
    bool HasActiveTransaction { get; }

    /// <summary>
    /// Initiates a new physical database transaction asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous transaction initiation.</returns>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes all tracked modifications or pending in-memory state changes to the underlying database without closing the transaction.
    /// Adapters without in-memory change trackers (e.g., ADO.NET) should complete immediately as a no-op.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous flush operation.</returns>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists state changes and commits the active physical transaction asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous commit operation.</returns>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the active physical transaction asynchronously, reverting pending changes.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous rollback operation.</returns>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}