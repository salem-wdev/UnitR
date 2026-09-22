namespace UnitR.Core.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnitR.Abstractions.Adapters;
using UnitR.Abstractions.Contracts;
using UnitR.Abstractions.Services;
using UnitR.Core.ErrorHandling;
using UnitR.Core.Options;

/// <summary>
/// Implements the transactional unit of work orchestration.
/// Coordinates business workloads, automatic transaction boundaries via <see cref="ITransactionAdapter"/>,
/// background event harvesting via <see cref="IDomainEventProvider"/>, and pre/post-commit event dispatching.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly IMediator _mediator;
    private readonly ILogger<UnitOfWork> _logger;
    private readonly UnitROptions _options;
    private readonly ITransactionAdapter _transactionAdapter;
    private readonly IDomainEventProvider? _domainEventProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnitOfWork"/> class.
    /// </summary>
    /// <param name="mediator">The MediatR mediator instance used for publishing notifications.</param>
    /// <param name="logger">The logger instance for diagnostics and structured error tracking.</param>
    /// <param name="options">The configuration options controlling post-commit execution behaviors.</param>
    /// <param name="transactionAdapter">The persistence adapter managing the physical transaction lifecycle.</param>
    /// <param name="domainEventProvider">Optional provider for auto-harvesting uncommitted domain events from entities.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are null.</exception>
    public UnitOfWork(
        IMediator mediator,
        ILogger<UnitOfWork> logger,
        IOptions<UnitROptions> options,
        ITransactionAdapter transactionAdapter,
        IDomainEventProvider? domainEventProvider = null)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new UnitROptions();
        _transactionAdapter = transactionAdapter ?? throw new ArgumentNullException(nameof(transactionAdapter));
        _domainEventProvider = domainEventProvider;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(
        Func<Task> action,
        CancellationToken cancellationToken = default,
        params INotification[] explicitEvents)
    {
        ArgumentNullException.ThrowIfNull(action);

        // Delegate to the generic overload returning a discardable null value
        await ExecuteAsync<object?>(async () =>
        {
            await action().ConfigureAwait(false);
            return null;
        }, cancellationToken, explicitEvents).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult> ExecuteAsync<TResult>(
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default,
        params INotification[] explicitEvents)
    {
        ArgumentNullException.ThrowIfNull(action);

        TResult result;
        var postCommitEvents = new List<IPostCommitNotification>();

        // Check if this instance owns the outermost transaction boundary to support nested executions safely
        bool isTransactionOwner = !_transactionAdapter.HasActiveTransaction;

        try
        {
            // 1. Begin the underlying transaction boundary if no active transaction exists
            if (isTransactionOwner)
            {
                await _transactionAdapter.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Initiated physical database transaction boundary.");
            }

            // 2. Execute the user's workload (mutates domain aggregates and registers state changes)
            _logger.LogDebug("Executing transactional business workload.");
            result = await action().ConfigureAwait(false);

            // 3. Harvest events: Collect background entity events and merge with explicitly passed notifications
            var backgroundEvents = _domainEventProvider?.GetDomainEvents() ?? Array.Empty<INotification>();
            var explicitList = explicitEvents ?? Array.Empty<INotification>();

            var allEvents = backgroundEvents.Concat(explicitList).ToList();

            var preCommitEvents = allEvents.OfType<IPreCommitNotification>().ToList();
            postCommitEvents.AddRange(allEvents.OfType<IPostCommitNotification>());

            // 4. Dispatch pre-commit notifications sequentially within the active transaction
            if (preCommitEvents.Count > 0)
            {
                _logger.LogDebug("Publishing {Count} pre-commit domain event(s).", preCommitEvents.Count);

                foreach (var preEvent in preCommitEvents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await _mediator.Publish(preEvent, cancellationToken).ConfigureAwait(false);
                }
            }

            // 5. Commit the physical transaction boundary
            if (isTransactionOwner)
            {
                await _transactionAdapter.CommitAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Committed physical database transaction boundary successfully.");
            }
        }
        catch (Exception ex)
        {
            // 6. Rollback automatically on workload or pre-commit failures
            _logger.LogError(ex, "An error occurred during transaction execution. Initiating automatic rollback.");

            if (isTransactionOwner)
            {
                try
                {
                    await _transactionAdapter.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogCritical(rollbackEx, "Critical error: Failed to rollback physical transaction.");
                }
            }

            // Rethrow original business exception to let upstream callers handle it
            throw;
        }

        // 7. Clear auto-harvested domain events only after a successful commit
        _domainEventProvider?.ClearDomainEvents();

        // 8. Dispatch post-commit notifications outside the transaction boundary
        if (postCommitEvents.Count > 0)
        {
            _logger.LogDebug(
                "Publishing {Count} post-commit domain event(s) using execution mode: {Mode}.",
                postCommitEvents.Count,
                _options.PostCommitMode);

            await PublishPostCommitEventsAsync(postCommitEvents, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Handles post-commit notifications sequentially or concurrently based on runtime configuration.
    /// </summary>
    private async Task PublishPostCommitEventsAsync(
        IReadOnlyList<IPostCommitNotification> events,
        CancellationToken cancellationToken)
    {
        var exceptions = new List<Exception>();

        if (_options.PostCommitMode == PostCommitExecutionMode.Sequential)
        {
            // Execute handlers sequentially one after another
            foreach (var postEvent in events)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await _mediator.Publish(postEvent, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(
                        ex,
                        "Failed to execute sequential post-commit event of type {EventType}.",
                        postEvent.GetType().Name);

                    exceptions.Add(ex);
                }
            }
        }
        else
        {
            // Execute handlers concurrently across thread pool tasks
            var tasks = events.Select(async postEvent =>
            {
                try
                {
                    await _mediator.Publish(postEvent, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(
                        ex,
                        "Failed to execute concurrent post-commit event of type {EventType}.",
                        postEvent.GetType().Name);

                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        // Apply error resilience policy
        if (exceptions.Count > 0 && _options.PostCommitErrorBehavior == PostCommitErrorBehavior.LogAndThrow)
        {
            throw new PostCommitExecutionException(exceptions);
        }
    }
}