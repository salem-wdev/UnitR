namespace UnitR.Abstractions.Contracts;

using MediatR;

/// <summary>
/// Defines a domain event that must be dispatched and handled synchronously
/// within the active database transaction boundary prior to persistence commit.
/// </summary>
/// <remarks>
/// Any unhandled exception thrown during the execution of associated pre-commit handlers
/// triggers an immediate transaction rollback, preventing data corruption and ghost side-effects.
/// </remarks>
public interface IPreCommitNotification : INotification;