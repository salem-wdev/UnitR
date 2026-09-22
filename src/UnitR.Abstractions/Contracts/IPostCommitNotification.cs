namespace UnitR.Abstractions.Contracts;

using MediatR;

/// <summary>
/// Defines a domain event that must only be dispatched after the underlying database
/// transaction has successfully committed to persistent storage.
/// </summary>
/// <remarks>
/// Post-commit executions are fault-isolated. Handlers encountering errors will not
/// invalidate or roll back the committed database transaction state.
/// </remarks>
public interface IPostCommitNotification : INotification;