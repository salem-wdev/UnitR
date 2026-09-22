namespace UnitR.Core.Options;

/// <summary>
/// Specifies how the orchestration engine handles exceptions occurring in post-commit handlers.
/// </summary>
public enum PostCommitErrorBehavior
{
    /// <summary>
    /// Logs the error and suppresses the exception so the caller's main flow is not broken.
    /// </summary>
    LogAndSuppress,

    /// <summary>
    /// Logs the error and throws a <see cref="ErrorHandling.PostCommitExecutionException"/>.
    /// </summary>
    LogAndThrow
}