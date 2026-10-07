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
/// Comprehensive test suite focused exclusively on the lifecycle, state management, 
/// execution scopes (nesting), and transaction ownership of the UnitOfWork.
/// </summary>
public class UnitOfWorkLifecycleTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ILogger<UnitOfWork>> _loggerMock;
    private readonly Mock<ITransactionAdapter> _adapterMock;
    private readonly IOptions<UnitROptions> _options;

    public UnitOfWorkLifecycleTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _loggerMock = new Mock<ILogger<UnitOfWork>>();
        _adapterMock = new Mock<ITransactionAdapter>();
        _options = Options.Create(new UnitROptions());
    }

    private UnitOfWork CreateSut()
    {
        return new UnitOfWork(
            _mediatorMock.Object,
            _loggerMock.Object,
            _options,
            _adapterMock.Object);
    }

    // =========================================================================
    // --- 1. Instantiation & Null Checks ---
    // =========================================================================

    [Fact]
    public void Constructor_WithNullDependencies_ShouldThrowArgumentNullException()
    {
        // Assert: Ensure the orchestrator cannot be created without required dependencies
        Assert.Throws<ArgumentNullException>(() => new UnitOfWork(null!, _loggerMock.Object, _options, _adapterMock.Object));
        Assert.Throws<ArgumentNullException>(() => new UnitOfWork(_mediatorMock.Object, null!, _options, _adapterMock.Object));
        Assert.Throws<ArgumentNullException>(() => new UnitOfWork(_mediatorMock.Object, _loggerMock.Object, _options, null!));
    }

    // =========================================================================
    // --- 2. State Validation & Premature Execution ---
    // =========================================================================

    [Fact]
    public async Task CommitAsync_WithoutBeginAsync_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        Func<Task> act = async () => await sut.CommitAsync();

        // Assert: A unit of work cannot be committed if it hasn't been started
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*BeginAsync first*");
    }

    [Fact]
    public async Task CallingMethods_AfterDisposal_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var sut = CreateSut();
        await sut.DisposeAsync();

        // Act & Assert: Prevent interacting with a disposed unit of work
        await sut.Invoking(s => s.BeginAsync()).Should().ThrowAsync<ObjectDisposedException>();
        await sut.Invoking(s => s.CommitAsync()).Should().ThrowAsync<ObjectDisposedException>();
    }

    // =========================================================================
    // --- 3. Transaction Ownership (Ambient vs Initiated) ---
    // =========================================================================

    [Fact]
    public async Task BeginAsync_WhenNoActiveTransactionExists_ShouldInitiatePhysicalTransactionAsOwner()
    {
        // Arrange: No ambient transaction exists
        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(false);
        var sut = CreateSut();

        // Act
        await sut.BeginAsync();

        // Assert: The orchestrator must claim ownership and initiate a new physical transaction
        _adapterMock.Verify(a => a.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BeginAsync_WhenActiveTransactionAlreadyExists_ShouldEnlistWithoutInitiatingNewTransaction()
    {
        // Arrange: An ambient transaction already exists (e.g., from an external middleware)
        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(true);
        var sut = CreateSut();

        // Act
        await sut.BeginAsync();

        // Assert: The orchestrator must enlist as a non-owner and NOT initiate a new physical transaction
        _adapterMock.Verify(a => a.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // =========================================================================
    // --- 4. Nesting & Boundary Deferral ---
    // =========================================================================

    [Fact]
    public async Task BeginAsync_WhenCalledMultipleTimes_ShouldOnlyBeginPhysicalTransactionOnce()
    {
        // Arrange
        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(false);
        var sut = CreateSut();

        // Act: Simulating a root call followed by multiple nested service calls
        await sut.BeginAsync(); // Level 1
        await sut.BeginAsync(); // Level 2
        await sut.BeginAsync(); // Level 3

        // Assert: Only the root call triggers the physical database transaction
        _adapterMock.Verify(a => a.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommitAsync_WhenInNestedScope_ShouldDeferPhysicalCommitToRootScope()
    {
        // Arrange
        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(false);
        var sut = CreateSut();

        await sut.BeginAsync(); // Root
        await sut.BeginAsync(); // Nested

        // Act: Nested commit
        await sut.CommitAsync();

        // Assert: Physical commit is deferred; database must not be hit yet
        _adapterMock.Verify(a => a.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _adapterMock.Verify(a => a.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // =========================================================================
    // --- 5. Root Commits (Owner vs Non-Owner) ---
    // =========================================================================

    [Fact]
    public async Task CommitAsync_WhenRootScopeCompletes_ShouldExecutePhysicalCommitIfOwner()
    {
        // Arrange: We are the owners of the transaction
        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(false);
        var sut = CreateSut();

        await sut.BeginAsync(); // Root
        await sut.BeginAsync(); // Nested
        await sut.CommitAsync(); // Nested commits (deferred)

        // Act: Root commits
        await sut.CommitAsync();

        // Assert: Changes are saved and the physical transaction is committed
        _adapterMock.Verify(a => a.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _adapterMock.Verify(a => a.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommitAsync_WhenRootScopeCompletes_ShouldSaveButNotCommitPhysicallyIfNotOwner()
    {
        // Arrange: We enlisted in an external transaction (NOT the owner)
        _adapterMock.SetupGet(a => a.HasActiveTransaction).Returns(true);
        var sut = CreateSut();

        await sut.BeginAsync(); // Root

        // Act: Root commits
        await sut.CommitAsync();

        // Assert: We must save our changes, but leave the physical commit to the external owner
        _adapterMock.Verify(a => a.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _adapterMock.Verify(a => a.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}