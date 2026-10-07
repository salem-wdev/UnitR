namespace UnitR.Core.Options;

/// <summary>
/// Global configuration options for the UnitR orchestration engine.
/// </summary>
public sealed class UnitROptions
{
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