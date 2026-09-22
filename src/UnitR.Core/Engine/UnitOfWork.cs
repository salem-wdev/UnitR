namespace UnitR.Core.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnitR.Abstractions.Contracts;
using UnitR.Abstractions.Services;
using UnitR.Core.ErrorHandling;
using UnitR.Core.Options;

/// <summary>
/// Implements the transactional unit of work orchestration, seamlessly combining
/// auto-harvested domain events from <see cref="IDomainEventProvider"/> with explicitly provided notifications.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly IMediator _mediator;
    private readonly ILogger<UnitOfWork> _logger;
    private readonly UnitROptions _options;
    private readonly IDomainEventProvider? _domainEventProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnitOfWork"/> class.
    /// </summary>
    /// <param name="mediator">The MediatR mediator instance used for publishing notifications.</param>
    /// <param name="logger">The logger instance for diagnostics and error tracking.</param>
    /// <param name="options">The configuration options controlling post-commit behavior.</param>
    /// <param name="domainEventProvider">Optional provider for auto-harvesting uncommitted domain events.</param>
    /// <exception cref="ArgumentNullException">Thrown when mediator or logger is null.</exception>
    public UnitOfWork(
        IMediator mediator,
        ILogger<UnitOfWork> logger,
        IOptions<UnitROptions> options,
        IDomainEventProvider? domainEventProvider = null)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new UnitROptions();
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

        // 1. Harvest both auto-collected events from the provider and explicitly passed notifications
        var autoEvents = _domainEventProvider?.GetDomainEvents() ?? Array.Empty<INotification>();
        var explicitList = explicitEvents ?? Array.Empty<INotification>();

        var allEvents = autoEvents.Concat(explicitList).ToList();

        // 2. Segregate notifications into pre-commit and post-commit pipelines
        var preCommitEvents = allEvents.OfType<IPreCommitNotification>().ToList();
        var postCommitEvents = allEvents.OfType<IPostCommitNotification>().ToList();

        // 3. Dispatch pre-commit events sequentially within the transactional boundary
        if (preCommitEvents.Count > 0)
        {
            _logger.LogDebug("Publishing {Count} pre-commit domain event(s).", preCommitEvents.Count);

            foreach (var preEvent in preCommitEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _mediator.Publish(preEvent, cancellationToken).ConfigureAwait(false);
            }
        }

        // 4. Execute the primary business workload (e.g., entity state changes and persistence commit)
        _logger.LogDebug("Executing transactional workload.");
        TResult result = await action().ConfigureAwait(false);

        // 5. Purge harvested events from the provider to avoid duplicate execution in subsequent calls
        _domainEventProvider?.ClearDomainEvents();

        // 6. Dispatch post-commit events outside the transaction boundary using configured execution mode
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
    /// <param name="events">The collection of post-commit notifications to publish.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous publication process.</returns>
    /// <exception cref="PostCommitExecutionException">
    /// Thrown when handler failures occur and <see cref="PostCommitErrorBehavior.LogAndThrow"/> is active.
    /// </exception>
    private async Task PublishPostCommitEventsAsync(
        IReadOnlyList<IPostCommitNotification> events,
        CancellationToken cancellationToken)
    {
        var exceptions = new List<Exception>();

        if (_options.PostCommitMode == PostCommitExecutionMode.Sequential)
        {
            // Execute handlers serially one after another
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
            // Execute handlers concurrently using Task.WhenAll
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