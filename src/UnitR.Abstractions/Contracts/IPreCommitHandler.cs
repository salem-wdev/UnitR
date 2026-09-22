namespace UnitR.Abstractions.Contracts;

using MediatR;

/// <summary>
/// Defines a handler for domain events that must be executed within the active transaction boundary, before the commit.
/// </summary>
/// <typeparam name="TEvent">The type of pre-commit event being handled.</typeparam>
public interface IPreCommitHandler<TEvent> : INotificationHandler<TEvent>
    where TEvent : IPreCommitNotification
{
}