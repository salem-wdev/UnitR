namespace UnitR.Abstractions.Contracts;

using System.Collections.Generic;

/// <summary>
/// Defines a contract for types that collect and expose domain events,
/// allowing automated harvesting before and after unit of work transactions.
/// </summary>
public interface IDomainEventProvider
{
    /// <summary>
    /// Retrieves all uncommitted domain events currently recorded by this instance.
    /// </summary>
    /// <returns>A read-only collection of pending domain events.</returns>
    IReadOnlyCollection<IUnitREvent> GetDomainEvents();

    /// <summary>
    /// Clears all recorded domain events after successful processing to prevent duplicate execution.
    /// </summary>
    void ClearDomainEvents();
}