namespace UnitR.Abstractions.Events;

/// <summary>
/// Marks a domain event that must be dispatched only after the database transaction has committed successfully.
/// </summary>
public interface IPostCommitEvent : IUnitREvent
{
}