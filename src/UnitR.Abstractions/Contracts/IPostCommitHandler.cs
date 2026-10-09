namespace UnitR.Abstractions.Contracts;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Defines an asynchronous handler for domain events that must be executed only after the transaction is successfully committed.
/// </summary>
/// <typeparam name="TEvent">The specific type of post-commit event being handled.</typeparam>
public interface IPostCommitHandler<in TEvent>
    where TEvent : IPostCommitEvent
{
    /// <summary>
    /// Handles the post-commit domain event asynchronously outside the transaction scope.
    /// </summary>
    /// <param name="event">The domain event instance.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}