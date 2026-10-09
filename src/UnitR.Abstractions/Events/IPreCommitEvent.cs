namespace UnitR.Abstractions.Events;

/// <summary>
/// Marks a domain event that must be dispatched and executed inside the active transaction boundary
/// before physical database persistence occurs.
/// </summary>
public interface IPreCommitEvent : IUnitREvent
{
}