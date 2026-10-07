namespace UnitR.Abstractions.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;

/// <summary>
/// Defines a lightweight unit-of-work contract for managing transactions and dispatching events.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Initiates the transaction boundary.
    /// </summary>
    Task BeginAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Concludes the work, dispatching pre-commit events, persisting changes, and executing post-commit events.
    /// </summary>
    Task CommitAsync(
        CancellationToken cancellationToken = default,
        params INotification[] explicitEvents);
}