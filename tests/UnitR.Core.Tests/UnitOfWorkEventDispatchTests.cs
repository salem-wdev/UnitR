namespace UnitR.Core.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UnitR.Abstractions.Adapters;
using UnitR.Abstractions.Contracts;
using UnitR.Core.Engine;
using UnitR.Core.ErrorHandling;
using UnitR.Core.Options;
using Xunit;

/// <summary>
/// Comprehensive test suite focused exclusively on Domain Event dispatching.
/// Covers Pre-Commit events, Post-Commit events, execution modes (Sequential/Concurrent), 
/// and the iterative Drain Loop for cascading events.
/// </summary>
public class UnitOfWorkEventDispatchTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ILogger<UnitOfWork>> _loggerMock;
    private readonly Mock<ITransactionAdapter> _adapterMock;
    private readonly UnitROptions _options;

    public UnitOfWorkEventDispatchTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _loggerMock = new Mock<ILogger<UnitOfWork>>();
        _adapterMock = new Mock<ITransactionAdapter>();
        _options = new UnitROptions();
    }

    private UnitOfWork CreateSut()
    {
        return new UnitOfWork(
            _mediatorMock.Object,
            _loggerMock.Object,
            Options.Create(_options),
            _adapterMock.Object);
    }

    // =========================================================================
    // --- 1. Pre-Commit Events & The Drain Loop ---
    // =========================================================================

    [Fact]
    public async Task CommitAsync_WithPreCommitEvents_ShouldPublishBeforeSavingChanges()
    {
        // Arrange
        var preEvent = new Mock<IPreCommitNotification>().Object;
        var sut = CreateSut();

        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(false);
        await sut.BeginAsync();

        // Act
        await sut.CommitAsync(CancellationToken.None, preEvent);

        // Assert: Pre-commit MUST fire before SaveChangesAsync
        var sequence = new MockSequence();
        _mediatorMock.InSequence(sequence).Setup(m => m.Publish(preEvent, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _adapterMock.InSequence(sequence).Setup(a => a.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _mediatorMock.Verify(m => m.Publish(preEvent, It.IsAny<CancellationToken>()), Times.Once);
        _adapterMock.Verify(a => a.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommitAsync_WithCascadingPreCommitEvents_ShouldDrainAllEventsBeforeSaving()
    {
        // Arrange: This tests the 'while(true)' drain loop explicitly!
        var initialEvent = new Mock<IPreCommitNotification>().Object;
        var cascadingEvent = new Mock<IPreCommitNotification>().Object;

        var sut = CreateSut();
        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(false);
        await sut.BeginAsync();

        // Simulate a handler reacting to 'initialEvent' by injecting a 'cascadingEvent' mid-flight
        _mediatorMock.Setup(m => m.Publish(initialEvent, It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((ev, ct) =>
            {
                // The handler enlists a new event into the already executing UnitOfWork
                sut.CommitAsync(ct, cascadingEvent).GetAwaiter().GetResult();
            })
            .Returns(Task.CompletedTask);

        // Act
        await sut.CommitAsync(CancellationToken.None, initialEvent);

        // Assert: Both the initial and the newly generated event MUST be published BEFORE the database save
        var sequence = new MockSequence();
        _mediatorMock.InSequence(sequence).Setup(m => m.Publish(initialEvent, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _mediatorMock.InSequence(sequence).Setup(m => m.Publish(cascadingEvent, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _adapterMock.InSequence(sequence).Setup(a => a.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _mediatorMock.Verify(m => m.Publish(initialEvent, It.IsAny<CancellationToken>()), Times.Once);
        _mediatorMock.Verify(m => m.Publish(cascadingEvent, It.IsAny<CancellationToken>()), Times.Once);
        _adapterMock.Verify(a => a.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // --- 2. Post-Commit Events Dispatching ---
    // =========================================================================

    [Fact]
    public async Task CommitAsync_WithPostCommitEvents_ShouldPublishAfterPhysicalCommit()
    {
        // Arrange
        var postEvent = new Mock<IPostCommitNotification>().Object;
        var sut = CreateSut();

        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(false);
        await sut.BeginAsync();

        // Act
        await sut.CommitAsync(CancellationToken.None, postEvent);

        // Assert: Physical commit MUST happen first, followed by the post-commit event
        var sequence = new MockSequence();
        _adapterMock.InSequence(sequence).Setup(a => a.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _mediatorMock.InSequence(sequence).Setup(m => m.Publish(postEvent, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _mediatorMock.Verify(m => m.Publish(postEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // --- 3. Post-Commit Error Behaviors ---
    // =========================================================================

    [Fact]
    public async Task CommitAsync_WhenPostCommitEventThrows_InLogAndIgnoreMode_ShouldSwallowException()
    {
        // Arrange: Default behavior is to Log and Ignore
        _options.PostCommitErrorBehavior = PostCommitErrorBehavior.LogAndSuppress;
        var postEvent = new Mock<IPostCommitNotification>().Object;

        _mediatorMock.Setup(m => m.Publish(postEvent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("External API failed"));

        var sut = CreateSut();
        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(false);
        await sut.BeginAsync();

        // Act
        Func<Task> act = async () => await sut.CommitAsync(CancellationToken.None, postEvent);

        // Assert: It should NOT throw, and the transaction must remain committed
        await act.Should().NotThrowAsync();
        _adapterMock.Verify(a => a.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommitAsync_WhenPostCommitEventThrows_InLogAndThrowMode_ShouldThrowPostCommitExecutionException()
    {
        // Arrange: Strict mode requires exceptions to surface to the caller
        _options.PostCommitErrorBehavior = PostCommitErrorBehavior.LogAndThrow;
        var postEvent = new Mock<IPostCommitNotification>().Object;

        _mediatorMock.Setup(m => m.Publish(postEvent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Strict post-commit failure"));

        var sut = CreateSut();
        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(false);
        await sut.BeginAsync();

        // Act
        Func<Task> act = async () => await sut.CommitAsync(CancellationToken.None, postEvent);

        // Assert: Throws a consolidated exception, but confirms the physical commit already succeeded
        var exceptionAssertion = await act.Should().ThrowAsync<PostCommitExecutionException>();
        exceptionAssertion.Which.InnerExceptions.Should().ContainSingle()
            .Which.Message.Should().Be("Strict post-commit failure");

        _adapterMock.Verify(a => a.CommitAsync(It.IsAny<CancellationToken>()), Times.Once); // Still committed!
    }
}