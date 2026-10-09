namespace UnitR.Abstractions.Domain;

using System.Collections.Generic;
using UnitR.Abstractions.Contracts;

/// <summary>
/// Defines the contract for aggregate roots that generate and encapsulate domain events.
/// Enables automatic event harvesting by persistence adapters during the transaction lifecycle.
/// </summary>
public interface IAggregateRoot
{
    /// <summary>
    /// Gets the immutable collection of domain events raised by this aggregate instance.
    /// </summary>
    IReadOnlyList<IUnitREvent> DomainEvents { get; }

    /// <summary>
    /// Clears all recorded domain events after they have been harvested by the transaction adapter.
    /// </summary>
    void ClearDomainEvents();
}