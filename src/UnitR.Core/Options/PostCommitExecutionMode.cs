namespace UnitR.Core.Options;

/// <summary>
/// Defines how post-commit event notifications are executed.
/// </summary>
public enum PostCommitExecutionMode
{
    /// <summary>
    /// Executes post-commit handlers sequentially one after another.
    /// </summary>
    Sequential,

    /// <summary>
    /// Executes post-commit handlers concurrently using Task.WhenAll.
    /// </summary>
    Parallel
}