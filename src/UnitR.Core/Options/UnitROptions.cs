namespace UnitR.Core.Options;

/// <summary>
/// Global configuration options for the UnitR orchestration engine.
/// </summary>
public sealed class UnitROptions
{
    private int _maxPreCommitDrainIterations = 50;

    /// <summary>
    /// Gets or sets the maximum allowed iterations when draining cascading pre-commit domain events.
    /// Protects against infinite loops caused by circular event publishing chains.
    /// Default value is 50.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the assigned value is less than 1.</exception>
    public int MaxPreCommitDrainIterations
    {
        get => _maxPreCommitDrainIterations;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "MaxPreCommitDrainIterations must be at least 1.");
            }

            _maxPreCommitDrainIterations = value;
        }
    }

    /// <summary>
    /// Gets or sets the execution mode for post-commit events.
    /// Defaults to <see cref="PostCommitExecutionMode.Sequential"/>.
    /// </summary>
    public PostCommitExecutionMode PostCommitMode { get; set; } = PostCommitExecutionMode.Sequential;

    /// <summary>
    /// Gets or sets the behavior when an exception occurs in a post-commit handler.
    /// Defaults to <see cref="PostCommitErrorBehavior.LogAndSuppress"/>.
    /// </summary>
    public PostCommitErrorBehavior PostCommitErrorBehavior { get; set; } = PostCommitErrorBehavior.LogAndSuppress;
}