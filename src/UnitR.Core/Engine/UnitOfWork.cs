namespace UnitR.Core.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UnitR.Abstractions.Adapters;
using UnitR.Abstractions.Events;
using UnitR.Abstractions.Services;
using UnitR.Core.ErrorHandling;
using UnitR.Core.Options;

/// <summary>
/// Implements the transactional unit of work orchestrator.
/// Coordinates transaction boundaries, nested execution scopes, safe rollbacks,
/// deferred batching of domain events, and pre/post-commit event dispatching.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly IEventPublisherAdapter _eventPublisherAdapter;
    private readonly ILogger<UnitOfWork> _logger;
    private readonly UnitROptions _options;
    private readonly ITransactionAdapter _transactionAdapter;

    /// <summary>
    /// Tracks the current nesting depth of execution scopes to defer root commit operations.
    /// </summary>
    private int _nestingLevel;

    /// <summary>
    /// Accumulates domain events passed explicitly by callers across all nested operations.
    /// </summary>
    private readonly List<IUnitREvent> _accumulatedExplicitEvents = new();

    private bool _isCommitted;
    private bool _isRolledBack;
    private bool _isDisposed;
    private bool _isTransactionOwner;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnitOfWork"/> class.
    /// </summary>
    /// <param name="eventPublisherAdapter">The decoupled adapter used for event dispatching.</param>
    /// <param name="logger">The structured diagnostics logger.</param>
    /// <param name="options">Configuration options controlling post-commit behaviors.</param>
    /// <param name="transactionAdapter">The persistence adapter managing physical transaction boundaries.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required dependency is null.</exception>
    public UnitOfWork(
        IEventPublisherAdapter eventPublisherAdapter,
        IOptions<UnitROptions> options,
        ITransactionAdapter transactionAdapter,
        ILogger<UnitOfWork>? logger = null)
    {
        _eventPublisherAdapter = eventPublisherAdapter ?? throw new ArgumentNullException(nameof(eventPublisherAdapter));
        _logger = logger ?? NullLogger<UnitOfWork>.Instance;
        _options = options?.Value ?? new UnitROptions();
        _transactionAdapter = transactionAdapter ?? throw new ArgumentNullException(nameof(transactionAdapter));
    }

    /// <inheritdoc />
    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_isRolledBack)
        {
            throw new InvalidOperationException("Cannot enlist in a unit of work that has already failed and rolled back.");
        }

        // Only the outermost root execution initiates the physical transaction boundary
        if (_nestingLevel == 0)
        {
            _isTransactionOwner = !_transactionAdapter.HasActiveTransaction;

            try
            {
                if (_isTransactionOwner)
                {
                    await _transactionAdapter.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                    _logger.LogDebug("Initiated physical database transaction boundary as root owner.");
                }
                else
                {
                    _logger.LogDebug("Enlisted into existing ambient physical database transaction.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initiate transaction boundary. Initiating rollback cleanup.");
                await SafeRollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        else
        {
            _logger.LogDebug("Joined nested transactional scope. Current depth: {Level}.", _nestingLevel + 1);
        }

        _nestingLevel++;
    }

    /// <inheritdoc />
    public async Task CommitAsync(
        CancellationToken cancellationToken = default,
        params IUnitREvent[] explicitEvents)
    {
        ThrowIfDisposed();

        if (_nestingLevel <= 0)
        {
            throw new InvalidOperationException("Cannot commit a unit of work that has not been initiated. Call BeginAsync first.");
        }

        if (_isRolledBack)
        {
            throw new InvalidOperationException("Cannot commit a unit of work that has been marked as rolled back.");
        }

        if (_isCommitted)
        {
            return;
        }

        // Accumulate explicit events provided at this level for consolidated execution at the root level
        if (explicitEvents != null && explicitEvents.Length > 0)
        {
            _accumulatedExplicitEvents.AddRange(explicitEvents);
        }

        _nestingLevel--;

        // If inner operations are still active, defer the physical persistence to the root scope
        if (_nestingLevel > 0)
        {
            _logger.LogDebug("Nested scope completed. Deferring physical commit to root coordinator. Remaining depth: {Level}.", _nestingLevel);
            return;
        }

        // =========================================================================
        // --- Root Coordinator Execution Boundary ---
        // =========================================================================
        var postCommitEvents = new List<IPostCommitEvent>();

        try
        {
            // Drain loop: Process pre-commit domain events iteratively in batches.
            // This ensures that any cascading/chained events registered by handlers during execution
            // are fully consumed and executed within the active transaction before persisting changes.
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var preCommitBatch = _accumulatedExplicitEvents
                    .OfType<IPreCommitEvent>()
                    .ToList();

                // Break the drain loop once no further pre-commit events remain
                if (preCommitBatch.Count == 0)
                {
                    break;
                }

                // Immediately purge the batch from the accumulated collection to avoid duplicate processing
                foreach (var preEvent in preCommitBatch)
                {
                    _accumulatedExplicitEvents.Remove(preEvent);
                }

                _logger.LogDebug("Publishing a batch of {Count} pre-commit domain event(s).", preCommitBatch.Count);

                // Dispatch current batch sequentially inside the transaction boundary
                foreach (var preEvent in preCommitBatch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await _eventPublisherAdapter.PublishAsync(preEvent, cancellationToken).ConfigureAwait(false);
                }
            }

            // Extract all accumulated post-commit notifications once pre-commit operations stabilize
            postCommitEvents.AddRange(_accumulatedExplicitEvents.OfType<IPostCommitEvent>());

            // 2. Persist all changes to the underlying database within the transaction boundary
            await _transactionAdapter.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // 3. Commit the underlying physical database transaction if this instance owns the boundary
            if (_isTransactionOwner)
            {
                await _transactionAdapter.CommitAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Physical database transaction committed successfully by root coordinator.");
            }

            _isCommitted = true;
            _accumulatedExplicitEvents.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transaction execution failed during root commit. Triggering rollback.");
            await SafeRollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw; // Re-throw to propagate to upstream exception middleware
        }

        // 4. Dispatch post-commit notifications outside the transaction boundary
        if (postCommitEvents.Count > 0)
        {
            _logger.LogDebug(
                "Publishing {Count} post-commit domain event(s) using execution mode: {Mode}.",
                postCommitEvents.Count,
                _options.PostCommitMode);

            await PublishPostCommitEventsAsync(postCommitEvents, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Executes a safe, non-lethal rollback of the active transaction across all enlisted scopes.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token; defaults to None during cleanups.</param>
    private async Task SafeRollbackAsync(CancellationToken cancellationToken)
    {
        if (_isRolledBack)
        {
            return;
        }

        _isRolledBack = true;
        _accumulatedExplicitEvents.Clear();

        // Regardless of ownership, a failure in any enlisted scope must revoke the physical transaction
        if (_transactionAdapter.HasActiveTransaction)
        {
            try
            {
                await _transactionAdapter.RollbackAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Rolled back physical database transaction boundary.");
            }
            catch (Exception rollbackEx)
            {
                _logger.LogCritical(rollbackEx, "Critical error encountered while rolling back physical transaction.");
            }
        }
    }

    /// <summary>
    /// Dispatches post-commit notifications either sequentially or concurrently based on configured strategy.
    /// </summary>
    private async Task PublishPostCommitEventsAsync(
        IReadOnlyList<IPostCommitEvent> events,
        CancellationToken cancellationToken)
    {
        var exceptions = new List<Exception>();

        if (_options.PostCommitMode == PostCommitExecutionMode.Sequential)
        {
            foreach (var postEvent in events)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await _eventPublisherAdapter.PublishAsync(postEvent, cancellationToken).ConfigureAwait(false);
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
            var tasks = events.Select(async postEvent =>
            {
                try
                {
                    await _eventPublisherAdapter.PublishAsync(postEvent, cancellationToken).ConfigureAwait(false);
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

        // Consolidate exceptions if configured to fail on post-commit side-effect errors
        if (exceptions.Count > 0 && _options.PostCommitErrorBehavior == PostCommitErrorBehavior.LogAndThrow)
        {
            throw new PostCommitExecutionException(exceptions);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        // Safety fallback: If execution exited prematurely without an explicit commit, revert changes immediately
        if (!_isCommitted && !_isRolledBack)
        {
            _logger.LogWarning("Unit of work scope disposed without reaching CommitAsync. Performing fallback rollback.");
            await SafeRollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }

        if (_isTransactionOwner)
        {
            await _transactionAdapter.DisposeAsync().ConfigureAwait(false);
        }

        _isDisposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(UnitOfWork));
        }
    }
}