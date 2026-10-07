namespace UnitR.Core.ErrorHandling;

using System;
using System.Collections.Generic;

/// <summary>
/// Exception thrown or logged when one or more post-commit event handlers fail.
/// The primary database transaction remains safely committed.
/// </summary>
public sealed class PostCommitExecutionException : AggregateException
{
    public PostCommitExecutionException(IEnumerable<Exception> innerExceptions)
        : base("One or more post-commit handlers failed to execute. The transaction remains committed.", innerExceptions)
    {
    }
}