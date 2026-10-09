namespace UnitR.Abstractions.Events;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Defines an asynchronous handler for domain events executed within the active transaction boundary.
/// </summary>
/// <typeparam name="TEvent">The specific type of pre-commit event being handled.</typeparam>
public interface IPreCommitHandler<in TEvent>
    where TEvent : IPreCommitEvent
{
    /// <summary>
    /// Handles the pre-commit domain event asynchronously.
    /// </summary>
    /// <param name="event">The domain event instance.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}