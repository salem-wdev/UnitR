namespace UnitR.Abstractions.Contracts;

using MediatR;

/// <summary>
/// Defines a handler for domain events that must be executed only after the transaction is successfully committed.
/// </summary>
/// <typeparam name="TEvent">The type of post-commit event being handled.</typeparam>
public interface IPostCommitHandler<TEvent> : INotificationHandler<TEvent>
    where TEvent : IPostCommitNotification
{
}