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
using UnitR.Core.Engine;
using UnitR.Core.Options;
using Xunit;

/// <summary>
/// Comprehensive test suite focused exclusively on Failure Handling, Rollbacks, 
/// State Poisoning, and Resource Disposal (Memory/Connection Management).
/// </summary>
public class UnitOfWorkRollbackAndDisposeTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ILogger<UnitOfWork>> _loggerMock;
    private readonly Mock<ITransactionAdapter> _adapterMock;
    private readonly UnitROptions _options;

    public UnitOfWorkRollbackAndDisposeTests()
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
    // --- 1. Database Failures & Active Rollbacks ---
    // =========================================================================

    [Fact]
    public async Task CommitAsync_WhenDatabaseSaveFails_ShouldTriggerSafeRollbackAndRethrow()
    {
        // Arrange
        var sut = CreateSut();
        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(false);

        // Simulate a database failure (e.g., constraint violation, timeout)
        _adapterMock.Setup(a => a.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database save error"));

        await sut.BeginAsync();

        // Act
        Func<Task> act = async () => await sut.CommitAsync();

        // Assert: The exception must surface to the caller, AND a physical rollback must be commanded
        await act.Should().ThrowAsync<Exception>().WithMessage("Database save error");
        _adapterMock.Verify(a => a.RollbackAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task CommitAsync_WhenPhysicalCommitFails_ShouldTriggerSafeRollbackAndRethrow()
    {
        // Arrange
        var sut = CreateSut();
        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(false);

        // Save succeeds, but the actual Commit command to the DB engine fails
        _adapterMock.Setup(a => a.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _adapterMock.Setup(a => a.CommitAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection lost during commit"));

        await sut.BeginAsync();

        // Act
        Func<Task> act = async () => await sut.CommitAsync();

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("Database connection lost during commit");
        _adapterMock.Verify(a => a.RollbackAsync(CancellationToken.None), Times.Once);
    }

    // =========================================================================
    // --- 2. Poisoned State Validation (Post-Rollback) ---
    // =========================================================================

    [Fact]
    public async Task BeginAsync_WhenAlreadyRolledBack_ShouldThrowInvalidOperationException()
    {
        // Arrange: Force a rollback state
        var sut = CreateSut();
        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(false);
        _adapterMock.Setup(a => a.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("DB Error"));

        await sut.BeginAsync();
        try { await sut.CommitAsync(); } catch { /* Suppress exception to reach the poisoned state */ }

        // Act: Attempt to reuse the poisoned UnitOfWork
        Func<Task> act = async () => await sut.BeginAsync();

        // Assert: It must be blocked instantly
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already failed and rolled back*");
    }

    [Fact]
    public async Task CommitAsync_WhenAlreadyRolledBack_ShouldThrowInvalidOperationException()
    {
        // Arrange: Force a rollback state
        var sut = CreateSut();
        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(false);
        _adapterMock.Setup(a => a.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("DB Error"));

        await sut.BeginAsync();
        try { await sut.CommitAsync(); } catch { /* Suppress */ }

        // Act
        Func<Task> act = async () => await sut.CommitAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*marked as rolled back*");
    }

    // =========================================================================
    // --- 3. Disposal & Fallback Rollbacks ---
    // =========================================================================

    [Fact]
    public async Task DisposeAsync_WhenScopeNotCommitted_ShouldExecuteFallbackRollback()
    {
        // Arrange
        var sut = CreateSut();
        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(false);
        await sut.BeginAsync();

        // Act: The developer forgot to call CommitAsync(), or an unhandled exception occurred mid-flight
        await sut.DisposeAsync();

        // Assert: The orchestrator MUST automatically rollback to prevent partial/dirty data leaks
        _adapterMock.Verify(a => a.RollbackAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_WhenTransactionOwner_ShouldDisposeAdapter()
    {
        // Arrange
        var sut = CreateSut();
        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(false); // We are the owner
        await sut.BeginAsync();
        await sut.CommitAsync();

        // Act
        await sut.DisposeAsync();

        // Assert: The physical database transaction resource must be freed
        _adapterMock.Verify(a => a.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_WhenNotTransactionOwner_ShouldNotDisposeAdapter()
    {
        // Arrange
        var sut = CreateSut();
        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(true); // Ambient external transaction
        await sut.BeginAsync();
        await sut.CommitAsync();

        // Act
        await sut.DisposeAsync();

        // Assert: NEVER dispose a connection that belongs to an external middleware
        _adapterMock.Verify(a => a.DisposeAsync(), Times.Never);
    }

    // =========================================================================
    // --- 4. Critical Rollback Failures ---
    // =========================================================================

    [Fact]
    public async Task SafeRollback_WhenAdapterRollbackThrows_ShouldCatchAndLogCriticalWithoutCrashing()
    {
        // Arrange
        var sut = CreateSut();
        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(false);

        // 1. Initial save fails
        _adapterMock.Setup(a => a.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("Initial save error"));

        // 2. The rollback itself also fails (e.g., database completely went offline)
        var criticalException = new Exception("Fatal rollback crash");
        _adapterMock.Setup(a => a.RollbackAsync(It.IsAny<CancellationToken>())).ThrowsAsync(criticalException);

        await sut.BeginAsync();

        // Act
        Func<Task> act = async () => await sut.CommitAsync();

        // Assert: It should throw the original exception, but MUST NOT crash completely due to the rollback failing.
        await act.Should().ThrowAsync<Exception>().WithMessage("Initial save error");

        // Verify that the critical error was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Critical error encountered while rolling back")),
                criticalException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}