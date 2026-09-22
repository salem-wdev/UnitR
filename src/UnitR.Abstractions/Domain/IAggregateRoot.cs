namespace UnitR.Abstractions.Domain;

using System.Collections.Generic;
using MediatR;

/// <summary>
/// Defines the contract for aggregate roots that generate and encapsulate domain events.
/// Enables automatic event harvesting by persistence adapters during the transaction lifecycle.
/// </summary>
public interface IAggregateRoot
{
    /// <summary>
    /// Gets the immutable collection of domain events raised by this aggregate instance.
    /// </summary>
    IReadOnlyList<INotification> DomainEvents { get; }

    /// <summary>
    /// Clears all recorded domain events after they have been harvested by the transaction adapter.
    /// </summary>
    void ClearDomainEvents();
}