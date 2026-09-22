# ⚡ UnitR

**High-Performance Transaction Orchestration & Two-Phase Domain Event Engine for .NET**

</div>

---

## 📌 Executive Summary & Problem Space

Modern .NET applications built with **Clean Architecture** or **Modular Monolith** patterns frequently face an architectural friction between two core concepts:

1. **The Unit of Work Pattern:** Controls database transaction lifecycles to guarantee atomic persistence.
2. **The Mediator & Domain Events Pattern:** Decouples domain interactions using in-memory message publishing (e.g., MediatR).

This friction leads to the **Event Phase Mismatch**:
* **Dispatched too early (Pre-Persistence):** Side-effects (such as payment triggers, SMS notifications, or publishing to external message brokers) execute before the database commit succeeds. If the database transaction fails, external actions cannot be undone, leading to ghost/phantom side effects.
* **Dispatched too late (Post-Persistence):** Critical domain logic executed in response to an event cannot participate in the atomic database transaction boundary, eliminating the ability to trigger a unified rollback on failure.

**UnitR** solves this by unifying transaction orchestration with strongly-typed lifecycle pipelines, segregating events deterministically into **Pre-Commit** and **Post-Commit** phases.

---

## 🎯 Key Architectural Principles

* **🔒 Strict Type Safety:** No dynamic reflection dispatch or dummy context marker objects. Handlers are statically constrained at compile-time (`IPreCommitHandler<T>`, `IPostCommitHandler<T>`).
* **🌾 Hybrid Event Harvesting:** Combines events explicitly passed via execution parameters with events automatically harvested from EF Core `ChangeTracker` entities implementing `IAggregateRoot`.
* **🔄 Ambient Transaction Propagation:** Seamless nested `ExecuteAsync` execution. Root transactions maintain physical commit authority; nested scopes participate implicitly without deadlocks or premature commits.
* **🛡️ Post-Commit Fault Isolation:** Exceptions thrown in post-commit handlers do not roll back or compromise the persisted transaction; faults are safely routed to a dedicated resilience strategy (`IPostCommitFailureHandler`).
* **🧵 Thread-Safe In-Transaction Execution:** Pre-commit notifications execute sequentially to avoid concurrent operations on non-thread-safe database contexts (such as EF Core's `DbContext`).
* **⚡ Cancellation Propagation:** Strict end-to-end `CancellationToken` checks across all internal boundaries.

---

## 🔄 Execution Pipeline

```text
[ExecuteAsync Called]
         │
[Ambient Transaction Check] ──(Active Exists?)──► [Participate as Child Scope]
         │ (No - Is Root Scope)                             │
         ▼                                                  ▼
[Begin TransactionAsync]                            [Execute User Workload]
         │                                                  │
         ▼                                                  ▼
[Execute User Workload]                             [Collect Explicit Events]
         │                                                  │
         ▼                                                  ▼
[Harvest & Merge Events (Explicit + ChangeTracker)] ◄───(Delegate to Root)
         │
         ▼
[Dispatch IPreCommitNotification Events (Sequential)]
         │
         ├──────► (Exception Thrown) ──► [Full RollbackAsync] ──► [Rethrow Exception]
         ▼
[Commit TransactionAsync]
         │
         ▼
[Dispatch IPostCommitNotification Events (Sequential / Parallel)]
         │
         ├──────► (Exception Thrown) ──► [IPostCommitFailureHandler] ──► [Keep State Intact]
         ▼
[Complete & Return Output]
```

---

## 📦 Package Layout

| Package | Description |
| :--- | :--- |
| `UnitR.Abstractions` | Lightweight contracts (`IPreCommitNotification`, `IPostCommitNotification`, `IAggregateRoot`, `IUnitOfWork`, `ITransactionAdapter`). |
| `UnitR.Core` | Central execution engine (`UnitOfWorkExecutor`), fault isolation, and configuration options. |
| `UnitR.EntityFrameworkCore` | Native adapter for EF Core with automatic `ChangeTracker` event harvesting. |
| `UnitR.AdoNet` | Lightweight adapter for ADO.NET and Dapper transactions. |

---

## 🚀 Getting Started

### 1. Installation

Install the required packages based on your project requirements:

```bash
# Core engine & abstractions
dotnet add package UnitR.Core

# Choose your persistence adapter:
dotnet add package UnitR.EntityFrameworkCore
# or
dotnet add package UnitR.AdoNet
```

### 2. Dependency Injection Registration

In your `Program.cs` or composition root:

#### For Entity Framework Core:

```csharp
// Register MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Register UnitR with EF Core
builder.Services.AddUnitREntityFrameworkCore<ApplicationDbContext>(options =>
{
    // Execute post-commit events in parallel or sequentially (Default: Sequential)
    options.PostCommitMode = PostCommitExecutionMode.Parallel;
});
```

#### For ADO.NET / Dapper:

```csharp
builder.Services.AddScoped<DbConnection>(sp => new SqlConnection(connectionString));
builder.Services.AddUnitRAdoNet();
```

---

## 💻 Usage Guide

### 1. Define Phase-Segregated Events

```csharp
using UnitR.Abstractions.Contracts;

// Pre-Commit: Must succeed atomically within the active database transaction
public sealed record DeductTrafficPointsEvent(int DriverId, int Points) : IPreCommitNotification;

// Post-Commit: Dispatched only after the transaction is fully committed to disk
public sealed record ViolationIssuedPostEvent(int ViolationId, string PhoneNumber) : IPostCommitNotification;
```

### 2. Implement Compile-Time Constrained Handlers

```csharp
using UnitR.Abstractions.Contracts;

// Runs inside the transaction boundary
public sealed class DeductPointsHandler : IPreCommitHandler<DeductTrafficPointsEvent>
{
    private readonly IDriverRepository _driverRepo;

    public DeductPointsHandler(IDriverRepository driverRepo) => _driverRepo = driverRepo;

    public async Task Handle(DeductTrafficPointsEvent notification, CancellationToken ct)
    {
        await _driverRepo.DeductPointsAsync(notification.DriverId, notification.Points, ct);
    }
}

// Runs outside the transaction boundary (isolated side effect)
public sealed class NotificationHandler : IPostCommitHandler<ViolationIssuedPostEvent>
{
    private readonly ISmsService _smsService;

    public NotificationHandler(ISmsService smsService) => _smsService = smsService;

    public async Task Handle(ViolationIssuedPostEvent notification, CancellationToken ct)
    {
        await _smsService.SendAsync(notification.PhoneNumber, $"Violation #{notification.ViolationId} recorded.", ct);
    }
}
```

### 3. Coordinate in Application Services (Hybrid Pattern)

Entities implementing `IAggregateRoot` can internally stage domain events, or events can be supplied explicitly:

```csharp
using UnitR.Abstractions.Services;

public class IssueViolationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IViolationRepository _repository;

    public IssueViolationService(IUnitOfWork unitOfWork, IViolationRepository repository)
    {
        _unitOfWork = unitOfWork;
        _repository = repository;
    }

    public async Task<int> IssueAsync(IssueViolationCommand command, CancellationToken ct)
    {
        return await _unitOfWork.ExecuteAsync(async () =>
        {
            // 1. Business operation
            var violation = new Violation(command.DriverId, command.Amount);
            await _repository.AddAsync(violation, ct);

            // 2. Return result
            return violation.Id;
        },
        ct,
        // 3. Explicit lifecycle events (combined with harvested entity events)
        new DeductTrafficPointsEvent(command.DriverId, Points: 3),
        new ViolationIssuedPostEvent(command.ViolationId, command.PhoneNumber));
    }
}
```

---

## 🛡️ Resilience & Edge Cases Matrix

| Scenario / Edge Case | Engine Handling & Behavior | System Outcome |
| :--- | :--- | :--- |
| **Nested `ExecuteAsync` Calls** | Inspects `HasActiveTransaction`; child joins ambient context silently, delegating commit to root. | Eliminates transaction deadlocks; guarantees all-or-nothing atomicity. |
| **Exception in `IPreCommitHandler`** | Catches exception, executes `RollbackAsync`, and rethrows immediately. | Aborts database transaction; guarantees post-commit events never fire. |
| **Outage during `IPostCommitHandler`** | Intercepts exception, logs and routes it to `IPostCommitFailureHandler`. | API response succeeds; database state remains committed and uncorrupted. |
| **Client Disconnection** | Checks `ThrowIfCancellationRequested()` and propagates token downstream. | Halts execution immediately; triggers clean rollback and releases resources. |
| **Multiple PreCommit Handlers** | Executed via deterministic sequential iteration (`foreach` with `await`). | Guarantees complete thread-safety across non-thread-safe database contexts. |

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).<div align="center">

# ⚡ UnitR

**High-Performance Transaction Orchestration & Two-Phase Domain Event Engine for .NET**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Target Platform](https://img.shields.io/badge/.NET-8.0%20%7C%20C%23%2012-purple.svg)](https://dotnet.microsoft.com/)
[![Build Status](https://img.shields.io/badge/build-passing-brightgreen.svg)]()

</div>

---

## 📌 Executive Summary & Problem Space

Modern .NET applications built with **Clean Architecture** or **Modular Monolith** patterns frequently face an architectural friction between two core concepts:

1. **The Unit of Work Pattern:** Controls database transaction lifecycles to guarantee atomic persistence.
2. **The Mediator & Domain Events Pattern:** Decouples domain interactions using in-memory message publishing (e.g., MediatR).

This friction leads to the **Event Phase Mismatch**:
* **Dispatched too early (Pre-Persistence):** Side-effects (such as payment triggers, SMS notifications, or publishing to external message brokers) execute before the database commit succeeds. If the database transaction fails, external actions cannot be undone, leading to ghost/phantom side effects.
* **Dispatched too late (Post-Persistence):** Critical domain logic executed in response to an event cannot participate in the atomic database transaction boundary, eliminating the ability to trigger a unified rollback on failure.

**UnitR** solves this by unifying transaction orchestration with strongly-typed lifecycle pipelines, segregating events deterministically into **Pre-Commit** and **Post-Commit** phases.

---

## 🎯 Key Architectural Principles

* **🔒 Strict Type Safety:** No dynamic reflection dispatch or dummy context marker objects. Handlers are statically constrained at compile-time (`IPreCommitHandler<T>`, `IPostCommitHandler<T>`).
* **🌾 Hybrid Event Harvesting:** Combines events explicitly passed via execution parameters with events automatically harvested from EF Core `ChangeTracker` entities implementing `IAggregateRoot`.
* **🔄 Ambient Transaction Propagation:** Seamless nested `ExecuteAsync` execution. Root transactions maintain physical commit authority; nested scopes participate implicitly without deadlocks or premature commits.
* **🛡️ Post-Commit Fault Isolation:** Exceptions thrown in post-commit handlers do not roll back or compromise the persisted transaction; faults are safely routed to a dedicated resilience strategy (`IPostCommitFailureHandler`).
* **🧵 Thread-Safe In-Transaction Execution:** Pre-commit notifications execute sequentially to avoid concurrent operations on non-thread-safe database contexts (such as EF Core's `DbContext`).
* **⚡ Cancellation Propagation:** Strict end-to-end `CancellationToken` checks across all internal boundaries.

---

## 🔄 Execution Pipeline

```text
[ExecuteAsync Called]
         │
[Ambient Transaction Check] ──(Active Exists?)──► [Participate as Child Scope]
         │ (No - Is Root Scope)                             │
         ▼                                                  ▼
[Begin TransactionAsync]                            [Execute User Workload]
         │                                                  │
         ▼                                                  ▼
[Execute User Workload]                             [Collect Explicit Events]
         │                                                  │
         ▼                                                  ▼
[Harvest & Merge Events (Explicit + ChangeTracker)] ◄───(Delegate to Root)
         │
         ▼
[Dispatch IPreCommitNotification Events (Sequential)]
         │
         ├──────► (Exception Thrown) ──► [Full RollbackAsync] ──► [Rethrow Exception]
         ▼
[Commit TransactionAsync]
         │
         ▼
[Dispatch IPostCommitNotification Events (Sequential / Parallel)]
         │
         ├──────► (Exception Thrown) ──► [IPostCommitFailureHandler] ──► [Keep State Intact]
         ▼
[Complete & Return Output]
```

---

## 📦 Package Layout

| Package | Description |
| :--- | :--- |
| `UnitR.Abstractions` | Lightweight contracts (`IPreCommitNotification`, `IPostCommitNotification`, `IAggregateRoot`, `IUnitOfWork`, `ITransactionAdapter`). |
| `UnitR.Core` | Central execution engine (`UnitOfWorkExecutor`), fault isolation, and configuration options. |
| `UnitR.EntityFrameworkCore` | Native adapter for EF Core with automatic `ChangeTracker` event harvesting. |
| `UnitR.AdoNet` | Lightweight adapter for ADO.NET and Dapper transactions. |

---

## 🚀 Getting Started

### 1. Installation

Install the required packages based on your project requirements:

```bash
# Core engine & abstractions
dotnet add package UnitR.Core

# Choose your persistence adapter:
dotnet add package UnitR.EntityFrameworkCore
# or
dotnet add package UnitR.AdoNet
```

### 2. Dependency Injection Registration

In your `Program.cs` or composition root:

#### For Entity Framework Core:

```csharp
// Register MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Register UnitR with EF Core
builder.Services.AddUnitREntityFrameworkCore<ApplicationDbContext>(options =>
{
    // Execute post-commit events in parallel or sequentially (Default: Sequential)
    options.PostCommitMode = PostCommitExecutionMode.Parallel;
});
```

#### For ADO.NET / Dapper:

```csharp
builder.Services.AddScoped<DbConnection>(sp => new SqlConnection(connectionString));
builder.Services.AddUnitRAdoNet();
```

---

## 💻 Usage Guide

### 1. Define Phase-Segregated Events

```csharp
using UnitR.Abstractions.Contracts;

// Pre-Commit: Must succeed atomically within the active database transaction
public sealed record DeductTrafficPointsEvent(int DriverId, int Points) : IPreCommitNotification;

// Post-Commit: Dispatched only after the transaction is fully committed to disk
public sealed record ViolationIssuedPostEvent(int ViolationId, string PhoneNumber) : IPostCommitNotification;
```

### 2. Implement Compile-Time Constrained Handlers

```csharp
using UnitR.Abstractions.Contracts;

// Runs inside the transaction boundary
public sealed class DeductPointsHandler : IPreCommitHandler<DeductTrafficPointsEvent>
{
    private readonly IDriverRepository _driverRepo;

    public DeductPointsHandler(IDriverRepository driverRepo) => _driverRepo = driverRepo;

    public async Task Handle(DeductTrafficPointsEvent notification, CancellationToken ct)
    {
        await _driverRepo.DeductPointsAsync(notification.DriverId, notification.Points, ct);
    }
}

// Runs outside the transaction boundary (isolated side effect)
public sealed class NotificationHandler : IPostCommitHandler<ViolationIssuedPostEvent>
{
    private readonly ISmsService _smsService;

    public NotificationHandler(ISmsService smsService) => _smsService = smsService;

    public async Task Handle(ViolationIssuedPostEvent notification, CancellationToken ct)
    {
        await _smsService.SendAsync(notification.PhoneNumber, $"Violation #{notification.ViolationId} recorded.", ct);
    }
}
```

### 3. Coordinate in Application Services (Hybrid Pattern)

Entities implementing `IAggregateRoot` can internally stage domain events, or events can be supplied explicitly:

```csharp
using UnitR.Abstractions.Services;

public class IssueViolationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IViolationRepository _repository;

    public IssueViolationService(IUnitOfWork unitOfWork, IViolationRepository repository)
    {
        _unitOfWork = unitOfWork;
        _repository = repository;
    }

    public async Task<int> IssueAsync(IssueViolationCommand command, CancellationToken ct)
    {
        return await _unitOfWork.ExecuteAsync(async () =>
        {
            // 1. Business operation
            var violation = new Violation(command.DriverId, command.Amount);
            await _repository.AddAsync(violation, ct);

            // 2. Return result
            return violation.Id;
        },
        ct,
        // 3. Explicit lifecycle events (combined with harvested entity events)
        new DeductTrafficPointsEvent(command.DriverId, Points: 3),
        new ViolationIssuedPostEvent(command.ViolationId, command.PhoneNumber));
    }
}
```

---

## 🛡️ Resilience & Edge Cases Matrix

| Scenario / Edge Case | Engine Handling & Behavior | System Outcome |
| :--- | :--- | :--- |
| **Nested `ExecuteAsync` Calls** | Inspects `HasActiveTransaction`; child joins ambient context silently, delegating commit to root. | Eliminates transaction deadlocks; guarantees all-or-nothing atomicity. |
| **Exception in `IPreCommitHandler`** | Catches exception, executes `RollbackAsync`, and rethrows immediately. | Aborts database transaction; guarantees post-commit events never fire. |
| **Outage during `IPostCommitHandler`** | Intercepts exception, logs and routes it to `IPostCommitFailureHandler`. | API response succeeds; database state remains committed and uncorrupted. |
| **Client Disconnection** | Checks `ThrowIfCancellationRequested()` and propagates token downstream. | Halts execution immediately; triggers clean rollback and releases resources. |
| **Multiple PreCommit Handlers** | Executed via deterministic sequential iteration (`foreach` with `await`). | Guarantees complete thread-safety across non-thread-safe database contexts. |

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).