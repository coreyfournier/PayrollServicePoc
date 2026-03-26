# Integration Tests & CI Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add database integration tests for PayrollService, TransferService, and ListenerApi, plus a Kafka pipeline test, all running in GitHub Actions CI.

**Architecture:** Three new xUnit test projects using Testcontainers for MongoDB/MySQL, one Kafka pipeline test using docker-compose, three new GitHub Actions workflows (unit, integration, kafka-pipeline). Existing unit test patterns (xUnit + FluentAssertions + NSubstitute) are followed throughout.

**Tech Stack:** .NET 9.0, xUnit, FluentAssertions, NSubstitute, Testcontainers.MongoDb, Testcontainers.MySql, Confluent.Kafka, GitHub Actions

---

## Task 1: PayrollService.DatabaseTests — Project Setup & Fixture

**Files:**
- Create: `tests/PayrollService.DatabaseTests/PayrollService.DatabaseTests.csproj`
- Create: `tests/PayrollService.DatabaseTests/GlobalUsings.cs`
- Create: `tests/PayrollService.DatabaseTests/Fixtures/MongoDbFixture.cs`
- Create: `tests/PayrollService.DatabaseTests/TestDoubles/TestUnitOfWork.cs`
- Modify: `PayrollService.sln`

- [ ] **Step 1: Create the project file**

```xml
<!-- tests/PayrollService.DatabaseTests/PayrollService.DatabaseTests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="NSubstitute" Version="5.1.0" />
    <PackageReference Include="Testcontainers.MongoDb" Version="4.3.0" />
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\PayrollService.Infrastructure\PayrollService.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create GlobalUsings.cs**

```csharp
// tests/PayrollService.DatabaseTests/GlobalUsings.cs
global using Xunit;
global using FluentAssertions;
global using NSubstitute;
```

- [ ] **Step 3: Create TestUnitOfWork**

This captures domain events instead of publishing to Kafka. Implements the same `IUnitOfWork` interface that `MassTransitUnitOfWork` does.

```csharp
// tests/PayrollService.DatabaseTests/TestDoubles/TestUnitOfWork.cs
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Common;

namespace PayrollService.DatabaseTests.TestDoubles;

public class TestUnitOfWork : IUnitOfWork
{
    private readonly List<DomainEvent> _publishedEvents = new();
    public IReadOnlyList<DomainEvent> PublishedEvents => _publishedEvents.AsReadOnly();

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        var result = await operation();
        _publishedEvents.AddRange(entity.DomainEvents);
        entity.ClearDomainEvents();
        return result;
    }

    public async Task ExecuteAsync(Func<Task> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        await operation();
        _publishedEvents.AddRange(entity.DomainEvents);
        entity.ClearDomainEvents();
    }

    public void Clear() => _publishedEvents.Clear();
}
```

- [ ] **Step 4: Create MongoDbFixture**

Starts a MongoDB container once per test collection. Registers real infrastructure services with `IUnitOfWork` replaced by `TestUnitOfWork`.

```csharp
// tests/PayrollService.DatabaseTests/Fixtures/MongoDbFixture.cs
using Microsoft.Extensions.DependencyInjection;
using PayrollService.Application.Interfaces;
using PayrollService.Infrastructure;
using PayrollService.Infrastructure.Persistence;
using Testcontainers.MongoDb;

namespace PayrollService.DatabaseTests.Fixtures;

public class MongoDbFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder()
        .WithImage("mongo:7.0")
        .Build();

    public ServiceProvider Services { get; private set; } = null!;
    public TestUnitOfWork UnitOfWork { get; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(_container.GetConnectionString(), "payroll_test_db");

        // Replace IUnitOfWork with test double (removes Kafka dependency)
        services.AddScoped<IUnitOfWork>(_ => UnitOfWork);

        // Register mock IProducer for DI resolution test
        services.AddSingleton(Substitute.For<Confluent.Kafka.IProducer<string, string>>());

        Services = services.BuildServiceProvider();

        // Initialize MongoDB indexes
        var dbContext = Services.GetRequiredService<MongoDbContext>();
        await dbContext.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        Services?.Dispose();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("PayrollMongo")]
public class PayrollMongoCollection : ICollectionFixture<MongoDbFixture> { }
```

- [ ] **Step 5: Add project to solution**

Run: `dotnet sln PayrollService.sln add tests/PayrollService.DatabaseTests/PayrollService.DatabaseTests.csproj`

- [ ] **Step 6: Verify the project builds**

Run: `dotnet build tests/PayrollService.DatabaseTests/`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add tests/PayrollService.DatabaseTests/ PayrollService.sln
git commit -m "feat: add PayrollService.DatabaseTests project with MongoDb fixture"
```

---

## Task 2: PayrollService.DatabaseTests — DI Resolution & Employee CRUD Tests

**Files:**
- Create: `tests/PayrollService.DatabaseTests/DependencyInjectionTests.cs`
- Create: `tests/PayrollService.DatabaseTests/EmployeeCrudTests.cs`

- [ ] **Step 1: Write DI resolution test**

```csharp
// tests/PayrollService.DatabaseTests/DependencyInjectionTests.cs
using Microsoft.Extensions.DependencyInjection;
using PayrollService.Application.Interfaces;
using PayrollService.DatabaseTests.Fixtures;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Persistence;

namespace PayrollService.DatabaseTests;

[Collection("PayrollMongo")]
public class DependencyInjectionTests
{
    private readonly MongoDbFixture _fixture;

    public DependencyInjectionTests(MongoDbFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(typeof(IEmployeeRepository))]
    [InlineData(typeof(ITimeEntryRepository))]
    [InlineData(typeof(ITaxInformationRepository))]
    [InlineData(typeof(IDeductionRepository))]
    [InlineData(typeof(IUnitOfWork))]
    [InlineData(typeof(MongoDbContext))]
    public void Infrastructure_Services_Resolve(Type serviceType)
    {
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetService(serviceType);
        service.Should().NotBeNull($"{serviceType.Name} should be registered");
    }
}
```

- [ ] **Step 2: Write Employee CRUD tests**

```csharp
// tests/PayrollService.DatabaseTests/EmployeeCrudTests.cs
using Microsoft.Extensions.DependencyInjection;
using PayrollService.DatabaseTests.Fixtures;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Repositories;

namespace PayrollService.DatabaseTests;

[Collection("PayrollMongo")]
public class EmployeeCrudTests
{
    private readonly MongoDbFixture _fixture;

    public EmployeeCrudTests(MongoDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AddAndRetrieve_Employee_RoundTrips()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IEmployeeRepository>();

        var employee = Employee.Create("Test", "User", "test@example.com", PayType.Salary, 75000m, DateTime.UtcNow);
        var created = await repo.AddAsync(employee);

        var retrieved = await repo.GetByIdAsync(created.Id);
        retrieved.Should().NotBeNull();
        retrieved!.FirstName.Should().Be("Test");
        retrieved.LastName.Should().Be("User");
        retrieved.Email.Should().Be("test@example.com");
        retrieved.PayType.Should().Be(PayType.Salary);
        retrieved.PayRate.Should().Be(75000m);
    }

    [Fact]
    public async Task Update_Employee_PersistsChanges()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IEmployeeRepository>();

        var employee = Employee.Create("Before", "Update", "before@example.com", PayType.Hourly, 25m, DateTime.UtcNow);
        await repo.AddAsync(employee);

        employee.Update("After", "Update", "after@example.com", PayType.Hourly, 30m);
        await repo.UpdateAsync(employee);

        var retrieved = await repo.GetByIdAsync(employee.Id);
        retrieved!.FirstName.Should().Be("After");
        retrieved.PayRate.Should().Be(30m);
    }

    [Fact]
    public async Task Upsert_SameId_DoesNotDuplicate()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IEmployeeRepository>();

        var employee = Employee.Create("Upsert", "Test", "upsert@example.com", PayType.Salary, 50000m, DateTime.UtcNow);
        await repo.AddAsync(employee);
        // Second add with same entity — upsert should overwrite, not throw
        await repo.AddAsync(employee);

        var retrieved = await repo.GetByIdAsync(employee.Id);
        retrieved.Should().NotBeNull();
    }

    [Fact]
    public async Task DomainEvents_CapturedByTestUnitOfWork()
    {
        _fixture.UnitOfWork.Clear();
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IEmployeeRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<PayrollService.Application.Interfaces.IUnitOfWork>();

        var employee = Employee.Create("Event", "Test", "event@example.com", PayType.Salary, 60000m, DateTime.UtcNow);
        await uow.ExecuteAsync(
            async () => await repo.AddAsync(employee),
            employee);

        _fixture.UnitOfWork.PublishedEvents.Should().ContainSingle()
            .Which.EventType.Should().Be("employee.created");
    }
}
```

- [ ] **Step 3: Run tests to verify they pass**

Run: `dotnet test tests/PayrollService.DatabaseTests/ -v normal`
Expected: All tests pass (requires Docker running)

- [ ] **Step 4: Commit**

```bash
git add tests/PayrollService.DatabaseTests/
git commit -m "feat: add PayrollService DI resolution and Employee CRUD tests"
```

---

## Task 3: PayrollService.DatabaseTests — Remaining Entity CRUD Tests

**Files:**
- Create: `tests/PayrollService.DatabaseTests/TimeEntryCrudTests.cs`
- Create: `tests/PayrollService.DatabaseTests/TaxInformationCrudTests.cs`
- Create: `tests/PayrollService.DatabaseTests/DeductionCrudTests.cs`

- [ ] **Step 1: Write TimeEntry CRUD tests**

```csharp
// tests/PayrollService.DatabaseTests/TimeEntryCrudTests.cs
using Microsoft.Extensions.DependencyInjection;
using PayrollService.DatabaseTests.Fixtures;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Repositories;

namespace PayrollService.DatabaseTests;

[Collection("PayrollMongo")]
public class TimeEntryCrudTests
{
    private readonly MongoDbFixture _fixture;

    public TimeEntryCrudTests(MongoDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AddAndRetrieve_TimeEntry_RoundTrips()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITimeEntryRepository>();

        var employeeId = Guid.NewGuid();
        var clockIn = DateTime.UtcNow.AddHours(-8);
        var clockOut = DateTime.UtcNow;
        var entry = TimeEntry.Create(employeeId, clockIn, clockOut);
        await repo.AddAsync(entry);

        var retrieved = await repo.GetByIdAsync(entry.Id);
        retrieved.Should().NotBeNull();
        retrieved!.EmployeeId.Should().Be(employeeId);
        retrieved.HoursWorked.Should().BeApproximately(8, 0.01);
    }

    [Fact]
    public async Task Update_TimeEntry_OverwritesById()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITimeEntryRepository>();

        var employeeId = Guid.NewGuid();
        var entry = TimeEntry.Create(employeeId, DateTime.UtcNow.AddHours(-4), DateTime.UtcNow);
        await repo.AddAsync(entry);

        entry.UpdateTimes(DateTime.UtcNow.AddHours(-6), DateTime.UtcNow);
        await repo.UpdateAsync(entry);

        var retrieved = await repo.GetByIdAsync(entry.Id);
        retrieved!.HoursWorked.Should().BeApproximately(6, 0.01);
    }

    [Fact]
    public async Task GetByEmployeeId_ReturnsOnlyMatchingEntries()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITimeEntryRepository>();

        var employeeId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await repo.AddAsync(TimeEntry.Create(employeeId, DateTime.UtcNow.AddHours(-8), DateTime.UtcNow));
        await repo.AddAsync(TimeEntry.Create(otherId, DateTime.UtcNow.AddHours(-8), DateTime.UtcNow));

        var entries = await repo.GetByEmployeeIdAsync(employeeId);
        entries.Should().ContainSingle();
        entries.First().EmployeeId.Should().Be(employeeId);
    }
}
```

- [ ] **Step 2: Write TaxInformation CRUD tests**

```csharp
// tests/PayrollService.DatabaseTests/TaxInformationCrudTests.cs
using Microsoft.Extensions.DependencyInjection;
using PayrollService.DatabaseTests.Fixtures;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Repositories;

namespace PayrollService.DatabaseTests;

[Collection("PayrollMongo")]
public class TaxInformationCrudTests
{
    private readonly MongoDbFixture _fixture;

    public TaxInformationCrudTests(MongoDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AddAndRetrieve_TaxInformation_RoundTrips()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITaxInformationRepository>();

        var employeeId = Guid.NewGuid();
        var tax = TaxInformation.Create(employeeId, "Married", 2, 50m, "CA", "Married", 1, 25m);
        await repo.AddAsync(tax);

        var retrieved = await repo.GetByEmployeeIdAsync(employeeId);
        retrieved.Should().NotBeNull();
        retrieved!.FederalFilingStatus.Should().Be("Married");
        retrieved.State.Should().Be("CA");
        retrieved.AdditionalFederalWithholding.Should().Be(50m);
    }

    [Fact]
    public async Task Update_TaxInformation_PersistsChanges()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITaxInformationRepository>();

        var employeeId = Guid.NewGuid();
        var tax = TaxInformation.Create(employeeId, "Single", 1, 0m, "NY", "Single", 1, 0m);
        await repo.AddAsync(tax);

        tax.Update("Married", 2, 100m, "TX", "Married", 2, 50m);
        await repo.UpdateAsync(tax);

        var retrieved = await repo.GetByEmployeeIdAsync(employeeId);
        retrieved!.FederalFilingStatus.Should().Be("Married");
        retrieved.State.Should().Be("TX");
        retrieved.AdditionalFederalWithholding.Should().Be(100m);
    }
}
```

- [ ] **Step 3: Write Deduction CRUD tests**

```csharp
// tests/PayrollService.DatabaseTests/DeductionCrudTests.cs
using Microsoft.Extensions.DependencyInjection;
using PayrollService.DatabaseTests.Fixtures;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Repositories;

namespace PayrollService.DatabaseTests;

[Collection("PayrollMongo")]
public class DeductionCrudTests
{
    private readonly MongoDbFixture _fixture;

    public DeductionCrudTests(MongoDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AddAndRetrieve_Deduction_RoundTrips()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDeductionRepository>();

        var employeeId = Guid.NewGuid();
        var deduction = Deduction.Create(employeeId, DeductionType.Health, "Health Insurance", 100m, false);
        await repo.AddAsync(deduction);

        var retrieved = await repo.GetByIdAsync(deduction.Id);
        retrieved.Should().NotBeNull();
        retrieved!.DeductionType.Should().Be(DeductionType.Health);
        retrieved.Amount.Should().Be(100m);
        retrieved.IsPercentage.Should().BeFalse();
    }

    [Fact]
    public async Task GetByEmployeeId_ReturnsMultipleDeductions()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDeductionRepository>();

        var employeeId = Guid.NewGuid();
        await repo.AddAsync(Deduction.Create(employeeId, DeductionType.Health, "Health", 100m, false));
        await repo.AddAsync(Deduction.Create(employeeId, DeductionType.Retirement401k, "Retirement", 5m, true));

        var deductions = await repo.GetByEmployeeIdAsync(employeeId);
        deductions.Should().HaveCount(2);
    }

    [Fact]
    public async Task Deactivate_Deduction_SetsIsActiveFalse()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDeductionRepository>();

        var deduction = Deduction.Create(Guid.NewGuid(), DeductionType.Dental, "Dental", 50m, false);
        await repo.AddAsync(deduction);

        deduction.Deactivate();
        await repo.UpdateAsync(deduction);

        var retrieved = await repo.GetByIdAsync(deduction.Id);
        retrieved!.IsActive.Should().BeFalse();
    }
}
```

- [ ] **Step 4: Run all PayrollService database tests**

Run: `dotnet test tests/PayrollService.DatabaseTests/ -v normal`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add tests/PayrollService.DatabaseTests/
git commit -m "feat: add TimeEntry, TaxInformation, and Deduction CRUD tests"
```

---

## Task 4: TransferService.DatabaseTests — Project Setup & Fixture

**Files:**
- Create: `tests/TransferService.DatabaseTests/TransferService.DatabaseTests.csproj`
- Create: `tests/TransferService.DatabaseTests/GlobalUsings.cs`
- Create: `tests/TransferService.DatabaseTests/Fixtures/MongoDbFixture.cs`
- Create: `tests/TransferService.DatabaseTests/TestDoubles/MockExternalServices.cs`
- Modify: `PayrollService.sln`

- [ ] **Step 1: Create the project file**

```xml
<!-- tests/TransferService.DatabaseTests/TransferService.DatabaseTests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="NSubstitute" Version="5.1.0" />
    <PackageReference Include="Testcontainers.MongoDb" Version="4.3.0" />
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\TransferService.Infrastructure\TransferService.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create GlobalUsings.cs**

```csharp
// tests/TransferService.DatabaseTests/GlobalUsings.cs
global using Xunit;
global using FluentAssertions;
global using NSubstitute;
```

- [ ] **Step 3: Create mock external services**

```csharp
// tests/TransferService.DatabaseTests/TestDoubles/MockExternalServices.cs
using TransferService.Application.Interfaces;
using TransferService.Domain.Entities;

namespace TransferService.DatabaseTests.TestDoubles;

public class TestUnitOfWork : IUnitOfWork
{
    private readonly List<TransferService.Domain.Common.DomainEvent> _publishedEvents = new();
    public IReadOnlyList<TransferService.Domain.Common.DomainEvent> PublishedEvents => _publishedEvents.AsReadOnly();

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, TransferService.Domain.Common.Entity entity, CancellationToken cancellationToken = default)
    {
        var result = await operation();
        _publishedEvents.AddRange(entity.DomainEvents);
        entity.ClearDomainEvents();
        return result;
    }

    public async Task ExecuteAsync(Func<Task> operation, TransferService.Domain.Common.Entity entity, CancellationToken cancellationToken = default)
    {
        await operation();
        _publishedEvents.AddRange(entity.DomainEvents);
        entity.ClearDomainEvents();
    }

    public void Clear() => _publishedEvents.Clear();
}
```

- [ ] **Step 4: Create MongoDbFixture**

Must call `TransferMongoDbContext.InitializeAsync()` to create the partial unique index.

```csharp
// tests/TransferService.DatabaseTests/Fixtures/MongoDbFixture.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TransferService.Application.Interfaces;
using TransferService.Application.Options;
using TransferService.Infrastructure;
using TransferService.Infrastructure.Persistence;
using Testcontainers.MongoDb;

namespace TransferService.DatabaseTests.Fixtures;

public class MongoDbFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder()
        .WithImage("mongo:7.0")
        .Build();

    public ServiceProvider Services { get; private set; } = null!;
    public TestDoubles.TestUnitOfWork UnitOfWork { get; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TransferLimits:MaxPerPayPeriod"] = "5",
                ["TransferLimits:MaxAmountPerPayPeriod"] = "10000",
                ["TransferLimits:MaxPerDay"] = "1"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<TransferLimitsOptions>(config.GetSection("TransferLimits"));
        services.AddTransferInfrastructure(_container.GetConnectionString(), "transfer_test_db");

        // Replace services that depend on external infrastructure
        services.AddScoped<IUnitOfWork>(_ => UnitOfWork);
        services.AddScoped<IBankTransferService>(_ => Substitute.For<IBankTransferService>());
        services.AddScoped<IBalanceService>(_ => Substitute.For<IBalanceService>());
        services.AddScoped<ITransferEventPublisher>(_ => Substitute.For<ITransferEventPublisher>());

        // Mock Kafka producer for DI resolution test
        services.AddSingleton(Substitute.For<Confluent.Kafka.IProducer<string, string>>());

        Services = services.BuildServiceProvider();

        // Create indexes (including the partial unique index)
        var dbContext = Services.GetRequiredService<TransferMongoDbContext>();
        await dbContext.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        Services?.Dispose();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("TransferMongo")]
public class TransferMongoCollection : ICollectionFixture<MongoDbFixture> { }
```

- [ ] **Step 5: Add project to solution**

Run: `dotnet sln PayrollService.sln add tests/TransferService.DatabaseTests/TransferService.DatabaseTests.csproj`

- [ ] **Step 6: Verify build**

Run: `dotnet build tests/TransferService.DatabaseTests/`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add tests/TransferService.DatabaseTests/ PayrollService.sln
git commit -m "feat: add TransferService.DatabaseTests project with MongoDb fixture"
```

---

## Task 5: TransferService.DatabaseTests — DI, CRUD & Constraint Tests

**Files:**
- Create: `tests/TransferService.DatabaseTests/DependencyInjectionTests.cs`
- Create: `tests/TransferService.DatabaseTests/TransferCrudTests.cs`
- Create: `tests/TransferService.DatabaseTests/TransferConstraintTests.cs`
- Create: `tests/TransferService.DatabaseTests/BankAccountTests.cs`

- [ ] **Step 1: Write DI resolution tests**

```csharp
// tests/TransferService.DatabaseTests/DependencyInjectionTests.cs
using Microsoft.Extensions.DependencyInjection;
using TransferService.Application.Interfaces;
using TransferService.DatabaseTests.Fixtures;
using TransferService.Domain.Repositories;
using TransferService.Infrastructure.Persistence;

namespace TransferService.DatabaseTests;

[Collection("TransferMongo")]
public class DependencyInjectionTests
{
    private readonly MongoDbFixture _fixture;

    public DependencyInjectionTests(MongoDbFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(typeof(ITransferRepository))]
    [InlineData(typeof(IBankAccountRepository))]
    [InlineData(typeof(IEmployeeTransferLimitsRepository))]
    [InlineData(typeof(IUnitOfWork))]
    [InlineData(typeof(IBankTransferService))]
    [InlineData(typeof(ITransferValidationService))]
    [InlineData(typeof(ITransferEventPublisher))]
    [InlineData(typeof(IBalanceService))]
    [InlineData(typeof(TransferMongoDbContext))]
    public void Infrastructure_Services_Resolve(Type serviceType)
    {
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetService(serviceType);
        service.Should().NotBeNull($"{serviceType.Name} should be registered");
    }
}
```

- [ ] **Step 2: Write Transfer CRUD tests**

```csharp
// tests/TransferService.DatabaseTests/TransferCrudTests.cs
using Microsoft.Extensions.DependencyInjection;
using TransferService.DatabaseTests.Fixtures;
using TransferService.Domain.Entities;
using TransferService.Domain.Repositories;

namespace TransferService.DatabaseTests;

[Collection("TransferMongo")]
public class TransferCrudTests
{
    private readonly MongoDbFixture _fixture;

    public TransferCrudTests(MongoDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AddAndRetrieve_Transfer_RoundTrips()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

        var transfer = Transfer.Create(Guid.NewGuid(), 500m, 55, Guid.NewGuid());
        await repo.AddAsync(transfer);

        var retrieved = await repo.GetByIdAsync(transfer.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Amount.Should().Be(500m);
        retrieved.PayPeriodNumber.Should().Be(55);
        retrieved.Status.Should().Be(TransferService.Domain.Enums.TransferStatus.Initiated);
        retrieved.WorkflowSteps.Should().HaveCount(5);
    }

    [Fact]
    public async Task Update_Transfer_PersistsStateChange()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

        var transfer = Transfer.Create(Guid.NewGuid(), 200m, 55, Guid.NewGuid());
        await repo.AddAsync(transfer);

        transfer.MarkCompleted("BNK-20260320-abc12345");
        await repo.UpdateAsync(transfer);

        var retrieved = await repo.GetByIdAsync(transfer.Id);
        retrieved!.Status.Should().Be(TransferService.Domain.Enums.TransferStatus.Completed);
        retrieved.ExternalReferenceId.Should().Be("BNK-20260320-abc12345");
    }

    [Fact]
    public async Task WorkflowSteps_PersistAndDeserialize()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

        var transfer = Transfer.Create(Guid.NewGuid(), 100m, 55, Guid.NewGuid());
        transfer.CompleteWorkflowStep("Validation", "Passed");
        transfer.StartWorkflowStep("BalanceCheck");
        await repo.AddAsync(transfer);

        var retrieved = await repo.GetByIdAsync(transfer.Id);
        var validationStep = retrieved!.WorkflowSteps.Find(s => s.Name == "Validation");
        validationStep!.Status.Should().Be("Completed");
        var balanceStep = retrieved.WorkflowSteps.Find(s => s.Name == "BalanceCheck");
        balanceStep!.Status.Should().Be("InProgress");
    }
}
```

- [ ] **Step 3: Write constraint tests**

These verify the MongoDB partial unique index enforces one-in-progress-transfer-per-employee.

```csharp
// tests/TransferService.DatabaseTests/TransferConstraintTests.cs
using Microsoft.Extensions.DependencyInjection;
using TransferService.DatabaseTests.Fixtures;
using TransferService.Domain.Entities;
using TransferService.Domain.Exceptions;
using TransferService.Domain.Repositories;

namespace TransferService.DatabaseTests;

[Collection("TransferMongo")]
public class TransferConstraintTests
{
    private readonly MongoDbFixture _fixture;

    public TransferConstraintTests(MongoDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SecondInProgressTransfer_SameEmployee_ThrowsDuplicateException()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

        var employeeId = Guid.NewGuid();
        var first = Transfer.Create(employeeId, 100m, 55, Guid.NewGuid());
        await repo.AddAsync(first);

        var second = Transfer.Create(employeeId, 200m, 55, Guid.NewGuid());
        var act = () => repo.AddAsync(second);

        await act.Should().ThrowAsync<DuplicateInProgressTransferException>();
    }

    [Fact]
    public async Task CompletedTransfer_AllowsNewTransfer_SameEmployee()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

        var employeeId = Guid.NewGuid();
        var first = Transfer.Create(employeeId, 100m, 55, Guid.NewGuid());
        await repo.AddAsync(first);

        first.MarkCompleted("BNK-ref-123");
        await repo.UpdateAsync(first);

        var second = Transfer.Create(employeeId, 200m, 55, Guid.NewGuid());
        var act = () => repo.AddAsync(second);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FailedTransfer_AllowsNewTransfer_SameEmployee()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

        var employeeId = Guid.NewGuid();
        var first = Transfer.Create(employeeId, 100m, 55, Guid.NewGuid());
        await repo.AddAsync(first);

        first.MarkFailed("Insufficient funds");
        await repo.UpdateAsync(first);

        var second = Transfer.Create(employeeId, 200m, 55, Guid.NewGuid());
        var act = () => repo.AddAsync(second);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DifferentEmployees_CanHaveSimultaneousInProgressTransfers()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

        var first = Transfer.Create(Guid.NewGuid(), 100m, 55, Guid.NewGuid());
        var second = Transfer.Create(Guid.NewGuid(), 200m, 55, Guid.NewGuid());

        await repo.AddAsync(first);
        var act = () => repo.AddAsync(second);
        await act.Should().NotThrowAsync();
    }
}
```

- [ ] **Step 4: Write BankAccount tests**

```csharp
// tests/TransferService.DatabaseTests/BankAccountTests.cs
using Microsoft.Extensions.DependencyInjection;
using TransferService.DatabaseTests.Fixtures;
using TransferService.Domain.Entities;
using TransferService.Domain.Enums;
using TransferService.Domain.Repositories;

namespace TransferService.DatabaseTests;

[Collection("TransferMongo")]
public class BankAccountTests
{
    private readonly MongoDbFixture _fixture;

    public BankAccountTests(MongoDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AddAndRetrieve_BankAccount_RoundTrips()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBankAccountRepository>();

        var employeeId = Guid.NewGuid();
        var account = BankAccount.Create(employeeId, "Chase Bank", "****1234", "021000021", BankAccountType.Checking);
        await repo.AddAsync(account);

        var retrieved = await repo.GetByIdAsync(account.Id);
        retrieved.Should().NotBeNull();
        retrieved!.EmployeeId.Should().Be(employeeId);
        retrieved.BankName.Should().Be("Chase Bank");
        retrieved.AccountType.Should().Be(BankAccountType.Checking);
    }

    [Fact]
    public async Task GetByEmployeeId_ReturnsOnlyMatchingAccounts()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBankAccountRepository>();

        var employeeId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await repo.AddAsync(BankAccount.Create(employeeId, "Chase", "****1111", "021000021", BankAccountType.Checking));
        await repo.AddAsync(BankAccount.Create(otherId, "Chase", "****2222", "021000021", BankAccountType.Savings));

        var accounts = await repo.GetByEmployeeIdAsync(employeeId);
        accounts.Should().ContainSingle();
        accounts.First().EmployeeId.Should().Be(employeeId);
    }
}
```

- [ ] **Step 5: Run all TransferService database tests**

Run: `dotnet test tests/TransferService.DatabaseTests/ -v normal`
Expected: All tests pass

- [ ] **Step 6: Commit**

```bash
git add tests/TransferService.DatabaseTests/
git commit -m "feat: add TransferService DI, CRUD, and constraint tests"
```

---

## Task 6: TransferService.DatabaseTests — Validation & Limits Tests

**Files:**
- Create: `tests/TransferService.DatabaseTests/TransferValidationTests.cs`
- Create: `tests/TransferService.DatabaseTests/TransferLimitsRepositoryTests.cs`

- [ ] **Step 1: Write TransferValidationService tests**

These test the real `TransferValidationService` against a real MongoDB with real repositories.

```csharp
// tests/TransferService.DatabaseTests/TransferValidationTests.cs
using Microsoft.Extensions.DependencyInjection;
using TransferService.Application.Interfaces;
using TransferService.Application.Services;
using TransferService.DatabaseTests.Fixtures;
using TransferService.Domain.Entities;
using TransferService.Domain.Enums;
using TransferService.Domain.Repositories;

namespace TransferService.DatabaseTests;

[Collection("TransferMongo")]
public class TransferValidationTests
{
    private readonly MongoDbFixture _fixture;

    public TransferValidationTests(MongoDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Validate_NonExistentBankAccount_Fails()
    {
        using var scope = _fixture.Services.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<ITransferValidationService>();

        var request = new TransferValidationRequest(
            Guid.NewGuid(), 100m, 55, Guid.NewGuid());

        var result = await validator.ValidateAsync(request);
        result.CanTransfer.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.Contains("bank account"));
    }

    [Fact]
    public async Task Validate_WrongEmployeeBankAccount_Fails()
    {
        using var scope = _fixture.Services.CreateScope();
        var bankRepo = scope.ServiceProvider.GetRequiredService<IBankAccountRepository>();
        var validator = scope.ServiceProvider.GetRequiredService<ITransferValidationService>();

        var ownerEmployeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var account = BankAccount.Create(ownerEmployeeId, "Chase", "****1234", "021000021", BankAccountType.Checking);
        await bankRepo.AddAsync(account);

        var request = new TransferValidationRequest(
            otherEmployeeId, 100m, 55, account.Id);

        var result = await validator.ValidateAsync(request);
        result.CanTransfer.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.Contains("belong"));
    }

    [Fact]
    public async Task Validate_InProgressTransfer_ExcludesCurrentTransferId()
    {
        using var scope = _fixture.Services.CreateScope();
        var transferRepo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
        var bankRepo = scope.ServiceProvider.GetRequiredService<IBankAccountRepository>();
        var validator = scope.ServiceProvider.GetRequiredService<ITransferValidationService>();

        var employeeId = Guid.NewGuid();
        var account = BankAccount.Create(employeeId, "Chase", "****5678", "021000021", BankAccountType.Checking);
        await bankRepo.AddAsync(account);

        // Create an in-progress transfer
        var existing = Transfer.Create(employeeId, 100m, 55, account.Id);
        await transferRepo.AddAsync(existing);

        // Validating with the same transferId should NOT flag duplicate
        var request = new TransferValidationRequest(
            employeeId, 200m, 55, account.Id, existing.Id);

        var result = await validator.ValidateAsync(request);
        result.Reasons.Should().NotContain(r => r.Contains("already in progress"));
    }

    [Fact]
    public async Task Validate_DuplicateInProgressTransfer_Fails()
    {
        using var scope = _fixture.Services.CreateScope();
        var transferRepo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
        var bankRepo = scope.ServiceProvider.GetRequiredService<IBankAccountRepository>();
        var validator = scope.ServiceProvider.GetRequiredService<ITransferValidationService>();

        var employeeId = Guid.NewGuid();
        var account = BankAccount.Create(employeeId, "Chase", "****9012", "021000021", BankAccountType.Checking);
        await bankRepo.AddAsync(account);

        var existing = Transfer.Create(employeeId, 100m, 55, account.Id);
        await transferRepo.AddAsync(existing);

        // Different transferId — should flag duplicate
        var request = new TransferValidationRequest(
            employeeId, 200m, 55, account.Id, Guid.NewGuid());

        var result = await validator.ValidateAsync(request);
        result.CanTransfer.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.Contains("already"));
    }
}
```

- [ ] **Step 2: Write EmployeeTransferLimits repository tests**

```csharp
// tests/TransferService.DatabaseTests/TransferLimitsRepositoryTests.cs
using Microsoft.Extensions.DependencyInjection;
using TransferService.DatabaseTests.Fixtures;
using TransferService.Domain.Entities;
using TransferService.Domain.Repositories;

namespace TransferService.DatabaseTests;

[Collection("TransferMongo")]
public class TransferLimitsRepositoryTests
{
    private readonly MongoDbFixture _fixture;

    public TransferLimitsRepositoryTests(MongoDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task UpsertAndRetrieve_EmployeeTransferLimits_RoundTrips()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IEmployeeTransferLimitsRepository>();

        var employeeId = Guid.NewGuid();
        var limits = EmployeeTransferLimits.Create(employeeId, 10, 20000m, 3);
        await repo.UpsertAsync(limits);

        var retrieved = await repo.GetByEmployeeIdAsync(employeeId);
        retrieved.Should().NotBeNull();
        retrieved!.MaxTransfersPerPayPeriod.Should().Be(10);
        retrieved.MaxAmountPerPayPeriod.Should().Be(20000m);
        retrieved.MaxTransfersPerDay.Should().Be(3);
    }

    [Fact]
    public async Task Upsert_ExistingLimits_UpdatesValues()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IEmployeeTransferLimitsRepository>();

        var employeeId = Guid.NewGuid();
        var limits = EmployeeTransferLimits.Create(employeeId, 5, 10000m, 1);
        await repo.UpsertAsync(limits);

        limits.Update(15, 30000m, 5);
        await repo.UpsertAsync(limits);

        var retrieved = await repo.GetByEmployeeIdAsync(employeeId);
        retrieved!.MaxTransfersPerPayPeriod.Should().Be(15);
    }

    [Fact]
    public async Task Delete_RemovesLimits()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IEmployeeTransferLimitsRepository>();

        var employeeId = Guid.NewGuid();
        await repo.UpsertAsync(EmployeeTransferLimits.Create(employeeId, 5, 10000m, 1));
        await repo.DeleteAsync(employeeId);

        var retrieved = await repo.GetByEmployeeIdAsync(employeeId);
        retrieved.Should().BeNull();
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/TransferService.DatabaseTests/ -v normal`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add tests/TransferService.DatabaseTests/
git commit -m "feat: add TransferValidationService and EmployeeTransferLimits tests"
```

---

## Task 7: ListenerApi.DatabaseTests — Outbox Tests

**Files:**
- Create: `tests/ListenerApi.DatabaseTests/ListenerApi.DatabaseTests.csproj`
- Create: `tests/ListenerApi.DatabaseTests/GlobalUsings.cs`
- Create: `tests/ListenerApi.DatabaseTests/Fixtures/MySqlFixture.cs`
- Create: `tests/ListenerApi.DatabaseTests/OutboxTests.cs`
- Create: `tests/ListenerApi.DatabaseTests/MigrationTests.cs`
- Modify: `PayrollService.sln`

- [ ] **Step 1: Create the project file**

```xml
<!-- tests/ListenerApi.DatabaseTests/ListenerApi.DatabaseTests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Testcontainers.MySql" Version="4.3.0" />
    <PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="7.0.0" />
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\ListenerApi.Data\ListenerApi.Data.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create GlobalUsings.cs**

```csharp
// tests/ListenerApi.DatabaseTests/GlobalUsings.cs
global using Xunit;
global using FluentAssertions;
```

- [ ] **Step 3: Create MySqlFixture**

```csharp
// tests/ListenerApi.DatabaseTests/Fixtures/MySqlFixture.cs
using ListenerApi.Data.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MySql;

namespace ListenerApi.DatabaseTests.Fixtures;

public class MySqlFixture : IAsyncLifetime
{
    private readonly MySqlContainer _container = new MySqlBuilder()
        .WithImage("mysql:8.0")
        .WithCommand("--event-scheduler=ON")
        .Build();

    public ServiceProvider Services { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();

        var connectionString = _container.GetConnectionString();
        var serverVersion = ServerVersion.AutoDetect(connectionString);
        services.AddDbContext<ListenerDbContext>(options =>
            options.UseMySql(connectionString, serverVersion));

        Services = services.BuildServiceProvider();

        // Apply all EF Core migrations
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        Services?.Dispose();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("ListenerMySql")]
public class ListenerMySqlCollection : ICollectionFixture<MySqlFixture> { }
```

- [ ] **Step 4: Create Migration tests**

```csharp
// tests/ListenerApi.DatabaseTests/MigrationTests.cs
using ListenerApi.Data.DbContext;
using ListenerApi.DatabaseTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ListenerApi.DatabaseTests;

[Collection("ListenerMySql")]
public class MigrationTests
{
    private readonly MySqlFixture _fixture;

    public MigrationTests(MySqlFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AllMigrations_ApplyCleanly()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();

        var pending = await dbContext.Database.GetPendingMigrationsAsync();
        pending.Should().BeEmpty("all migrations should have been applied by the fixture");
    }
}
```

- [ ] **Step 5: Create Outbox tests**

```csharp
// tests/ListenerApi.DatabaseTests/OutboxTests.cs
using System.Text.Json;
using ListenerApi.Data.DbContext;
using ListenerApi.Data.Entities;
using ListenerApi.DatabaseTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace ListenerApi.DatabaseTests;

[Collection("ListenerMySql")]
public class OutboxTests
{
    private readonly MySqlFixture _fixture;

    public OutboxTests(MySqlFixture fixture) => _fixture = fixture;

    private async Task<Guid> SeedEmployeeAsync(ListenerDbContext dbContext)
    {
        var employeeId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        dbContext.EmployeeRecords.Add(new EmployeeRecord
        {
            Id = employeeId,
            FirstName = "Test",
            LastName = "Employee",
            Email = "test@example.com",
            PayType = "2",
            PayRate = 75000m,
            IsActive = true,
            LastEventType = "employee.created",
            LastEventTimestamp = now,
            LastEventId = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();
        return employeeId;
    }

    [Fact]
    public async Task AtomicWrite_TransferRecordAndOutboxMessage_BothPersist()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();

        var employeeId = await SeedEmployeeAsync(dbContext);
        var transferId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var record = new TransferRecord
        {
            Id = transferId,
            EmployeeId = employeeId,
            Amount = 500m,
            PayPeriodNumber = 55,
            Status = "Queued",
            InitiatedAt = now,
            UpdatedAt = now
        };
        dbContext.TransferRecords.Add(record);

        var outbox = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = employeeId.ToString(),
            Topic = "transfer-requests",
            Payload = JsonSerializer.Serialize(new
            {
                TransferId = transferId,
                EmployeeId = employeeId,
                Amount = 500m,
                PayPeriodNumber = 55,
                BankAccountId = Guid.NewGuid()
            }),
            CreatedAt = now
        };
        dbContext.OutboxMessages.Add(outbox);

        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        // Verify both were persisted
        var savedRecord = await dbContext.TransferRecords.FindAsync(transferId);
        savedRecord.Should().NotBeNull();
        savedRecord!.Status.Should().Be("Queued");

        var savedOutbox = await dbContext.OutboxMessages.FindAsync(outbox.Id);
        savedOutbox.Should().NotBeNull();
        savedOutbox!.Topic.Should().Be("transfer-requests");
    }

    [Fact]
    public async Task RolledBackTransaction_NeitherPersists()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();

        var employeeId = await SeedEmployeeAsync(dbContext);
        var transferId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        dbContext.TransferRecords.Add(new TransferRecord
        {
            Id = transferId,
            EmployeeId = employeeId,
            Amount = 100m,
            PayPeriodNumber = 55,
            Status = "Queued",
            InitiatedAt = now,
            UpdatedAt = now
        });

        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            Id = outboxId,
            AggregateId = employeeId.ToString(),
            Topic = "transfer-requests",
            Payload = "{}",
            CreatedAt = now
        });

        await dbContext.SaveChangesAsync();
        await transaction.RollbackAsync();

        // Use fresh context to verify nothing persisted
        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ListenerDbContext>();

        var record = await verifyDb.TransferRecords.FindAsync(transferId);
        record.Should().BeNull("transaction was rolled back");

        var outbox = await verifyDb.OutboxMessages.FindAsync(outboxId);
        outbox.Should().BeNull("transaction was rolled back");
    }

    [Fact]
    public async Task OutboxMessage_HasCorrectTopicAndAggregateId()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();

        var employeeId = Guid.NewGuid();
        var outbox = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = employeeId.ToString(),
            Topic = "transfer-requests",
            Payload = JsonSerializer.Serialize(new { EmployeeId = employeeId }),
            CreatedAt = DateTime.UtcNow
        };
        dbContext.OutboxMessages.Add(outbox);
        await dbContext.SaveChangesAsync();

        var saved = await dbContext.OutboxMessages.FindAsync(outbox.Id);
        saved!.Topic.Should().Be("transfer-requests");
        saved.AggregateId.Should().Be(employeeId.ToString());
    }

    [Fact]
    public async Task OutboxMessage_PayloadIsValidJson()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();

        var transferId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var bankAccountId = Guid.NewGuid();

        var payload = JsonSerializer.Serialize(new
        {
            TransferId = transferId,
            EmployeeId = employeeId,
            Amount = 250m,
            PayPeriodNumber = 55,
            BankAccountId = bankAccountId
        });

        var outbox = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = employeeId.ToString(),
            Topic = "transfer-requests",
            Payload = payload,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.OutboxMessages.Add(outbox);
        await dbContext.SaveChangesAsync();

        var saved = await dbContext.OutboxMessages.FindAsync(outbox.Id);
        var parsed = JsonDocument.Parse(saved!.Payload);
        parsed.RootElement.GetProperty("TransferId").GetGuid().Should().Be(transferId);
        parsed.RootElement.GetProperty("EmployeeId").GetGuid().Should().Be(employeeId);
        parsed.RootElement.GetProperty("Amount").GetDecimal().Should().Be(250m);
        parsed.RootElement.GetProperty("PayPeriodNumber").GetInt64().Should().Be(55);
        parsed.RootElement.GetProperty("BankAccountId").GetGuid().Should().Be(bankAccountId);
    }
}
```

- [ ] **Step 6: Add project to solution**

Run: `dotnet sln PayrollService.sln add tests/ListenerApi.DatabaseTests/ListenerApi.DatabaseTests.csproj`

- [ ] **Step 7: Run tests**

Run: `dotnet test tests/ListenerApi.DatabaseTests/ -v normal`
Expected: All tests pass

- [ ] **Step 8: Commit**

```bash
git add tests/ListenerApi.DatabaseTests/ PayrollService.sln
git commit -m "feat: add ListenerApi.DatabaseTests with outbox atomicity and migration tests"
```

---

## Task 8: GitHub Actions — Unit Tests Workflow

**Files:**
- Rename: `.github/workflows/ci.yml` → `.github/workflows/ci-unit.yml`

- [ ] **Step 1: Rename the existing CI workflow**

```bash
git mv .github/workflows/ci.yml .github/workflows/ci-unit.yml
```

- [ ] **Step 2: Update the workflow name**

Change line 1 of `.github/workflows/ci-unit.yml` from `name: CI` to `name: CI - Unit Tests`.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/
git commit -m "refactor: rename CI workflow to ci-unit.yml"
```

---

## Task 9: GitHub Actions — Integration Tests Workflow

**Files:**
- Create: `.github/workflows/ci-integration.yml`

- [ ] **Step 1: Create the integration tests workflow**

```yaml
# .github/workflows/ci-integration.yml
name: CI - Integration Tests

on:
  pull_request:
    branches: [master]
  push:
    branches: [master]

jobs:
  payroll-db-tests:
    name: PayrollService Database Tests
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            7.0.x
            9.0.x

      - name: Restore
        run: dotnet restore tests/PayrollService.DatabaseTests/

      - name: Build
        run: dotnet build tests/PayrollService.DatabaseTests/ --no-restore

      - name: Test
        run: dotnet test tests/PayrollService.DatabaseTests/ --no-build --logger "trx;LogFileName=payroll-db-results.trx"

  transfer-db-tests:
    name: TransferService Database Tests
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            7.0.x
            9.0.x

      - name: Restore
        run: dotnet restore tests/TransferService.DatabaseTests/

      - name: Build
        run: dotnet build tests/TransferService.DatabaseTests/ --no-restore

      - name: Test
        run: dotnet test tests/TransferService.DatabaseTests/ --no-build --logger "trx;LogFileName=transfer-db-results.trx"

  listener-db-tests:
    name: ListenerApi Database Tests
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            7.0.x
            9.0.x

      - name: Restore
        run: dotnet restore tests/ListenerApi.DatabaseTests/

      - name: Build
        run: dotnet build tests/ListenerApi.DatabaseTests/ --no-restore

      - name: Test
        run: dotnet test tests/ListenerApi.DatabaseTests/ --no-build --logger "trx;LogFileName=listener-db-results.trx"
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/ci-integration.yml
git commit -m "feat: add CI integration tests workflow"
```

---

## Task 10: Kafka Pipeline Test — Docker Compose & Project Setup

**Files:**
- Create: `docker-compose.kafka-test.yml`
- Create: `tests/KafkaPipeline.Tests/KafkaPipeline.Tests.csproj`
- Create: `tests/KafkaPipeline.Tests/GlobalUsings.cs`
- Create: `tests/KafkaPipeline.Tests/Fixtures/KafkaFixture.cs`
- Create: `tests/KafkaPipeline.Tests/Helpers/CloudEventProducer.cs`
- Create: `tests/KafkaPipeline.Tests/Helpers/TopicConsumer.cs`
- Modify: `PayrollService.sln`

- [ ] **Step 1: Create minimal docker-compose for Kafka tests**

```yaml
# docker-compose.kafka-test.yml
# Minimal Kafka stack for pipeline tests (no databases, no APIs, no Elasticsearch)
services:
  zookeeper:
    image: confluentinc/cp-zookeeper:7.5.0
    container_name: test-zookeeper
    environment:
      ZOOKEEPER_CLIENT_PORT: 2181
      ZOOKEEPER_TICK_TIME: 2000
    ports:
      - "2181:2181"
    networks:
      - test-network
    healthcheck:
      test: echo srvr | nc localhost 2181 || exit 1
      interval: 10s
      timeout: 5s
      retries: 5

  kafka:
    image: confluentinc/cp-kafka:7.5.0
    container_name: test-kafka
    depends_on:
      zookeeper:
        condition: service_healthy
    ports:
      - "29092:29092"
    environment:
      KAFKA_BROKER_ID: 1
      KAFKA_ZOOKEEPER_CONNECT: zookeeper:2181
      KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://kafka:9092,PLAINTEXT_HOST://localhost:29092
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: PLAINTEXT:PLAINTEXT,PLAINTEXT_HOST:PLAINTEXT
      KAFKA_INTER_BROKER_LISTENER_NAME: PLAINTEXT
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1
      KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR: 1
      KAFKA_TRANSACTION_STATE_LOG_MIN_ISR: 1
      KAFKA_AUTO_CREATE_TOPICS_ENABLE: "true"
    networks:
      - test-network
    healthcheck:
      test: kafka-broker-api-versions --bootstrap-server localhost:9092 || exit 1
      interval: 10s
      timeout: 10s
      retries: 10
      start_period: 30s

  kafka-init:
    image: confluentinc/cp-kafka:7.5.0
    container_name: test-kafka-init
    depends_on:
      kafka:
        condition: service_healthy
    entrypoint: ['/bin/sh', '-c']
    command: |
      "
      echo 'Creating Kafka topics...'
      kafka-topics --bootstrap-server kafka:9092 --create --if-not-exists --topic employee-events --partitions 3 --replication-factor 1
      kafka-topics --bootstrap-server kafka:9092 --create --if-not-exists --topic employee-net-pay --partitions 3 --replication-factor 1 --config cleanup.policy=compact
      kafka-topics --bootstrap-server kafka:9092 --create --if-not-exists --topic employee-info --partitions 3 --replication-factor 1 --config cleanup.policy=compact
      echo 'Topics created.'
      "
    networks:
      - test-network

  ksqldb-server:
    image: confluentinc/cp-ksqldb-server:7.7.1
    container_name: test-ksqldb-server
    ports:
      - "8088:8088"
    environment:
      KSQL_BOOTSTRAP_SERVERS: kafka:9092
      KSQL_LISTENERS: http://0.0.0.0:8088/
      KSQL_KSQL_SERVICE_ID: test_ksqldb_
      KSQL_KSQL_LOGGING_PROCESSING_STREAM_NAME: KSQL_PROCESSING_LOG
    depends_on:
      kafka:
        condition: service_healthy
    networks:
      - test-network
    healthcheck:
      test: curl -sf http://localhost:8088/info || exit 1
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 45s

  net-pay-processor:
    build:
      context: ./src/NetPayProcessor
      dockerfile: Dockerfile
    container_name: test-net-pay-processor
    environment:
      KAFKA_BOOTSTRAP_SERVERS: kafka:9092
      APPLICATION_ID: test-net-pay-processor
    depends_on:
      kafka:
        condition: service_healthy
    networks:
      - test-network

networks:
  test-network:
    driver: bridge
```

- [ ] **Step 2: Create project file**

```xml
<!-- tests/KafkaPipeline.Tests/KafkaPipeline.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Confluent.Kafka" Version="2.3.0" />
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create GlobalUsings.cs**

```csharp
// tests/KafkaPipeline.Tests/GlobalUsings.cs
global using Xunit;
global using FluentAssertions;
```

- [ ] **Step 4: Create CloudEventProducer helper**

Produces events in the same CloudEvent format as `PayrollService.Infrastructure.Messaging.CloudEventWrapper`.

```csharp
// tests/KafkaPipeline.Tests/Helpers/CloudEventProducer.cs
using System.Text.Json;
using System.Text.Json.Nodes;
using Confluent.Kafka;

namespace KafkaPipeline.Tests.Helpers;

public class CloudEventProducer : IDisposable
{
    private readonly IProducer<string, string> _producer;

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public CloudEventProducer(string bootstrapServers)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            ClientId = "kafka-pipeline-test"
        };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task ProduceAsync(string topic, string key, object entity)
    {
        var entityNode = JsonSerializer.SerializeToNode(entity, SerializeOptions);
        var cloudEvent = new JsonObject
        {
            ["type"] = "com.dapr.event.sent",
            ["source"] = "payroll-api",
            ["data"] = entityNode
        };

        var message = new Message<string, string>
        {
            Key = key,
            Value = cloudEvent.ToJsonString(SerializeOptions)
        };

        await _producer.ProduceAsync(topic, message);
    }

    public void Dispose() => _producer?.Dispose();
}
```

- [ ] **Step 5: Create TopicConsumer helper**

```csharp
// tests/KafkaPipeline.Tests/Helpers/TopicConsumer.cs
using System.Text.Json;
using Confluent.Kafka;

namespace KafkaPipeline.Tests.Helpers;

public class TopicConsumer : IDisposable
{
    private readonly IConsumer<string, string> _consumer;

    public TopicConsumer(string bootstrapServers, string groupId)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };
        _consumer = new ConsumerBuilder<string, string>(config).Build();
    }

    public void Subscribe(string topic) => _consumer.Subscribe(topic);

    public async Task<List<(string Key, JsonDocument Value)>> ConsumeUntilAsync(
        Func<List<(string Key, JsonDocument Value)>, bool> predicate,
        TimeSpan timeout)
    {
        var results = new List<(string Key, JsonDocument Value)>();
        var cts = new CancellationTokenSource(timeout);

        while (!cts.IsCancellationRequested)
        {
            try
            {
                var result = _consumer.Consume(TimeSpan.FromSeconds(1));
                if (result?.Message?.Value != null)
                {
                    var doc = JsonDocument.Parse(result.Message.Value);
                    results.Add((result.Message.Key, doc));
                    if (predicate(results))
                        return results;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return results;
    }

    public void Dispose() => _consumer?.Dispose();
}
```

- [ ] **Step 6: Create KafkaFixture**

Manages docker-compose lifecycle. Assumes containers are started externally by CI or developer.

```csharp
// tests/KafkaPipeline.Tests/Fixtures/KafkaFixture.cs
namespace KafkaPipeline.Tests.Fixtures;

public class KafkaFixture : IAsyncLifetime
{
    public string BootstrapServers => Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") ?? "localhost:29092";
    public string KsqlDbUrl => Environment.GetEnvironmentVariable("KSQLDB_URL") ?? "http://localhost:8088";

    public async Task InitializeAsync()
    {
        // Wait for Kafka to be ready
        using var adminClient = new Confluent.Kafka.AdminClientBuilder(
            new Confluent.Kafka.AdminClientConfig { BootstrapServers = BootstrapServers })
            .Build();

        var retries = 30;
        while (retries-- > 0)
        {
            try
            {
                var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(5));
                if (metadata.Brokers.Count > 0)
                    return;
            }
            catch
            {
                // Not ready yet
            }
            await Task.Delay(2000);
        }
        throw new Exception("Kafka did not become ready within timeout");
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition("KafkaPipeline")]
public class KafkaPipelineCollection : ICollectionFixture<KafkaFixture> { }
```

- [ ] **Step 7: Add to solution and verify build**

Run: `dotnet sln PayrollService.sln add tests/KafkaPipeline.Tests/KafkaPipeline.Tests.csproj && dotnet build tests/KafkaPipeline.Tests/`
Expected: Build succeeded

- [ ] **Step 8: Commit**

```bash
git add tests/KafkaPipeline.Tests/ docker-compose.kafka-test.yml PayrollService.sln
git commit -m "feat: add KafkaPipeline.Tests project with docker-compose and helpers"
```

---

## Task 11: Kafka Pipeline Test — NetPayProcessor & ksqlDB Tests

**Files:**
- Create: `tests/KafkaPipeline.Tests/NetPayProcessorTests.cs`
- Create: `tests/KafkaPipeline.Tests/KsqlDbEmployeeInfoTests.cs`

- [ ] **Step 1: Write salary employee net pay test**

The test produces events matching the seed data format and asserts the NetPayProcessor computes correct values.

```csharp
// tests/KafkaPipeline.Tests/NetPayProcessorTests.cs
using System.Text.Json;
using KafkaPipeline.Tests.Fixtures;
using KafkaPipeline.Tests.Helpers;

namespace KafkaPipeline.Tests;

[Collection("KafkaPipeline")]
public class NetPayProcessorTests
{
    private readonly KafkaFixture _fixture;

    public NetPayProcessorTests(KafkaFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SalaryEmployee_ProducesCorrectNetPay()
    {
        var employeeId = Guid.NewGuid().ToString();
        using var producer = new CloudEventProducer(_fixture.BootstrapServers);

        // Produce employee event (salary, $75,000/year, 40 hours/period)
        await producer.ProduceAsync("employee-events", employeeId, new
        {
            Id = employeeId,
            FirstName = "Test",
            LastName = "Salary",
            Email = "test.salary@example.com",
            PayType = 2, // Salary
            PayRate = 75000.0,
            PayPeriodHours = 40.0,
            IsActive = true,
            HireDate = "2024-01-15T00:00:00Z",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            DomainEvents = new[]
            {
                new { EventType = "employee.created", EventId = Guid.NewGuid().ToString(), OccurredOn = DateTime.UtcNow.ToString("O") }
            }
        });

        // Produce tax info (married, CA)
        await producer.ProduceAsync("employee-events", employeeId, new
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeId = employeeId,
            FederalFilingStatus = "Married",
            FederalAllowances = 2,
            AdditionalFederalWithholding = 0.0,
            State = "CA",
            StateFilingStatus = "Married",
            StateAllowances = 1,
            AdditionalStateWithholding = 0.0,
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            DomainEvents = new[]
            {
                new { EventType = "taxinfo.created", EventId = Guid.NewGuid().ToString(), OccurredOn = DateTime.UtcNow.ToString("O") }
            }
        });

        // Produce deduction (health $100 fixed)
        await producer.ProduceAsync("employee-events", employeeId, new
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeId = employeeId,
            DeductionType = 1, // Health
            Description = "Health Insurance",
            Amount = 100.0,
            IsPercentage = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            DomainEvents = new[]
            {
                new { EventType = "deduction.created", EventId = Guid.NewGuid().ToString(), OccurredOn = DateTime.UtcNow.ToString("O") }
            }
        });

        // Produce deduction (401k 5%)
        await producer.ProduceAsync("employee-events", employeeId, new
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeId = employeeId,
            DeductionType = 4, // Retirement401k
            Description = "401k",
            Amount = 5.0,
            IsPercentage = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            DomainEvents = new[]
            {
                new { EventType = "deduction.created", EventId = Guid.NewGuid().ToString(), OccurredOn = DateTime.UtcNow.ToString("O") }
            }
        });

        // Consume from employee-net-pay (wait for message that includes deductions)
        using var consumer = new TopicConsumer(_fixture.BootstrapServers, $"test-netpay-{Guid.NewGuid():N}");
        consumer.Subscribe("employee-net-pay");

        var results = await consumer.ConsumeUntilAsync(
            messages => messages.Count(m => m.Key.Contains(employeeId)) >= 3,
            TimeSpan.FromSeconds(45));

        var netPayMessage = results.Last(m => m.Key.Contains(employeeId));
        var root = netPayMessage.Value.RootElement;

        // Gross pay = 75000 / 26 ≈ 2884.62
        var grossPay = root.GetProperty("GROSS_PAY").GetDouble();
        grossPay.Should().BeApproximately(2884.62, 0.1);

        // CA state tax ~= 9.3% of annualized/26
        var stateTax = root.GetProperty("STATE_TAX").GetDouble();
        stateTax.Should().BeGreaterThan(0);

        // Deductions: $100 fixed + 5% of gross (~$144.23)
        var totalDeductions = root.GetProperty("TOTAL_DEDUCTIONS").GetDouble();
        totalDeductions.Should().BeApproximately(100.0 + (grossPay * 0.05), 1.0);

        // Net pay = gross - federal - state - deductions
        var netPay = root.GetProperty("NET_PAY").GetDouble();
        netPay.Should().BeGreaterThan(0);
        netPay.Should().BeLessThan(grossPay);
    }

    [Fact]
    public async Task HourlyEmployee_WithTimeEntries_ProducesCorrectGross()
    {
        var employeeId = Guid.NewGuid().ToString();
        using var producer = new CloudEventProducer(_fixture.BootstrapServers);

        // Produce hourly employee ($28.50/hr)
        await producer.ProduceAsync("employee-events", employeeId, new
        {
            Id = employeeId,
            FirstName = "Test",
            LastName = "Hourly",
            Email = "test.hourly@example.com",
            PayType = 1, // Hourly
            PayRate = 28.50,
            PayPeriodHours = 40.0,
            IsActive = true,
            HireDate = "2024-01-15T00:00:00Z",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            DomainEvents = new[]
            {
                new { EventType = "employee.created", EventId = Guid.NewGuid().ToString(), OccurredOn = DateTime.UtcNow.ToString("O") }
            }
        });

        // Produce time entry (8 hours)
        var timeEntryId = Guid.NewGuid().ToString();
        var clockIn = DateTime.UtcNow.AddHours(-8);
        var clockOut = DateTime.UtcNow;
        await producer.ProduceAsync("employee-events", employeeId, new
        {
            Id = timeEntryId,
            EmployeeId = employeeId,
            ClockIn = clockIn.ToString("O"),
            ClockOut = clockOut.ToString("O"),
            HoursWorked = 8.0,
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            DomainEvents = new[]
            {
                new { EventType = "timeentry.created", EventId = Guid.NewGuid().ToString(), OccurredOn = DateTime.UtcNow.ToString("O") }
            }
        });

        // Consume
        using var consumer = new TopicConsumer(_fixture.BootstrapServers, $"test-hourly-{Guid.NewGuid():N}");
        consumer.Subscribe("employee-net-pay");

        var results = await consumer.ConsumeUntilAsync(
            messages => messages.Count(m => m.Key.Contains(employeeId)) >= 1,
            TimeSpan.FromSeconds(30));

        // Find the latest message for this employee that has time entry hours
        var latestMessages = results.Where(m => m.Key.Contains(employeeId)).ToList();
        latestMessages.Should().NotBeEmpty();

        var last = latestMessages.Last();
        var grossPay = last.Value.RootElement.GetProperty("GROSS_PAY").GetDouble();

        // Gross = 28.50 * 8 = 228.00
        grossPay.Should().BeApproximately(228.0, 0.1);
    }
}
```

- [ ] **Step 2: Write ksqlDB employee-info test**

This verifies ksqlDB processes `employee-events` into the `employee-info` compacted topic.

```csharp
// tests/KafkaPipeline.Tests/KsqlDbEmployeeInfoTests.cs
using System.Text.Json;
using KafkaPipeline.Tests.Fixtures;
using KafkaPipeline.Tests.Helpers;

namespace KafkaPipeline.Tests;

[Collection("KafkaPipeline")]
public class KsqlDbEmployeeInfoTests
{
    private readonly KafkaFixture _fixture;

    public KsqlDbEmployeeInfoTests(KafkaFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task EmployeeCreated_AppearsOnEmployeeInfoTopic()
    {
        var employeeId = Guid.NewGuid().ToString();
        using var producer = new CloudEventProducer(_fixture.BootstrapServers);

        await producer.ProduceAsync("employee-events", employeeId, new
        {
            Id = employeeId,
            FirstName = "Info",
            LastName = "Test",
            Email = "info.test@example.com",
            PayType = 2,
            PayRate = 60000.0,
            PayPeriodHours = 40.0,
            IsActive = true,
            HireDate = "2024-06-01T00:00:00Z",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            DomainEvents = new[]
            {
                new { EventType = "employee.created", EventId = Guid.NewGuid().ToString(), OccurredOn = DateTime.UtcNow.ToString("O") }
            }
        });

        // Consume from employee-info (ksqlDB output topic)
        using var consumer = new TopicConsumer(_fixture.BootstrapServers, $"test-info-{Guid.NewGuid():N}");
        consumer.Subscribe("employee-info");

        var results = await consumer.ConsumeUntilAsync(
            messages => messages.Any(m => m.Key.Contains(employeeId)),
            TimeSpan.FromSeconds(30));

        var infoMessage = results.Last(m => m.Key.Contains(employeeId));
        var root = infoMessage.Value.RootElement;

        // ksqlDB EMPLOYEE_INFO table materializes latest employee state
        // The exact field names depend on the ksqlDB schema (typically uppercased)
        root.TryGetProperty("FIRST_NAME", out var firstName).Should().BeTrue();
        firstName.GetString().Should().Be("Info");
    }
}
```

- [ ] **Step 4: Run tests locally** (requires docker-compose.kafka-test.yml services running)

```bash
docker compose -f docker-compose.kafka-test.yml up -d
# Wait for services to be healthy
sleep 60
# Submit ksqlDB statements
curl -X POST http://localhost:8088/ksql -H 'Content-Type: application/vnd.ksql.v1+json' -d '{"ksql": "'"$(cat ksqldb/statements.sql)"'", "streamsProperties": {}}'
# Run tests
dotnet test tests/KafkaPipeline.Tests/ -v normal
# Tear down
docker compose -f docker-compose.kafka-test.yml down -v
```

- [ ] **Step 5: Commit**

```bash
git add tests/KafkaPipeline.Tests/
git commit -m "feat: add NetPayProcessor and ksqlDB pipeline tests"
```

---

## Task 12: GitHub Actions — Kafka Pipeline Workflow

**Files:**
- Create: `.github/workflows/ci-kafka-pipeline.yml`

- [ ] **Step 1: Create the Kafka pipeline workflow**

```yaml
# .github/workflows/ci-kafka-pipeline.yml
name: CI - Kafka Pipeline Tests

on:
  push:
    branches: [master]

jobs:
  kafka-pipeline:
    name: Kafka Pipeline Tests
    runs-on: ubuntu-latest
    timeout-minutes: 15
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 9.0.x

      - name: Start Kafka stack
        run: docker compose -f docker-compose.kafka-test.yml up -d

      - name: Wait for Kafka to be healthy
        run: |
          echo "Waiting for Kafka..."
          for i in $(seq 1 60); do
            if docker exec test-kafka kafka-broker-api-versions --bootstrap-server localhost:9092 > /dev/null 2>&1; then
              echo "Kafka is ready."
              break
            fi
            echo "Attempt $i/60..."
            sleep 5
          done

      - name: Wait for ksqlDB to be healthy
        run: |
          echo "Waiting for ksqlDB..."
          for i in $(seq 1 60); do
            if curl -sf http://localhost:8088/info > /dev/null 2>&1; then
              echo "ksqlDB is ready."
              break
            fi
            echo "Attempt $i/60..."
            sleep 5
          done

      - name: Wait for topics to be created
        run: |
          echo "Waiting for kafka-init to complete..."
          sleep 10
          docker exec test-kafka kafka-topics --bootstrap-server localhost:9092 --list

      - name: Initialize ksqlDB
        run: |
          # Submit statements one at a time (ksqlDB REST API handles one statement per request)
          while IFS= read -r line || [ -n "$line" ]; do
            # Skip empty lines and comments
            [[ -z "$line" || "$line" =~ ^[[:space:]]*-- ]] && continue
            # Accumulate until we hit a semicolon
            stmt="${stmt:-}${stmt:+ }$line"
            if [[ "$line" =~ \;[[:space:]]*$ ]]; then
              echo "Executing: ${stmt:0:80}..."
              curl -sf -X POST http://localhost:8088/ksql \
                -H 'Content-Type: application/vnd.ksql.v1+json' \
                -d "{\"ksql\": $(echo "$stmt" | jq -Rs .), \"streamsProperties\": {}}" || true
              stmt=""
              sleep 2
            fi
          done < ksqldb/statements.sql

      - name: Restore test project
        run: dotnet restore tests/KafkaPipeline.Tests/

      - name: Build test project
        run: dotnet build tests/KafkaPipeline.Tests/ --no-restore

      - name: Run Kafka pipeline tests
        run: dotnet test tests/KafkaPipeline.Tests/ --no-build --logger "trx;LogFileName=kafka-pipeline-results.trx"
        env:
          KAFKA_BOOTSTRAP_SERVERS: localhost:29092
          KSQLDB_URL: http://localhost:8088

      - name: Tear down
        if: always()
        run: docker compose -f docker-compose.kafka-test.yml down -v
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/ci-kafka-pipeline.yml
git commit -m "feat: add CI Kafka pipeline tests workflow"
```

---

## Task 13: Verification — Run All Tests Locally

- [ ] **Step 1: Run PayrollService database tests**

Run: `dotnet test tests/PayrollService.DatabaseTests/ -v normal`
Expected: All tests pass

- [ ] **Step 2: Run TransferService database tests**

Run: `dotnet test tests/TransferService.DatabaseTests/ -v normal`
Expected: All tests pass

- [ ] **Step 3: Run ListenerApi database tests**

Run: `dotnet test tests/ListenerApi.DatabaseTests/ -v normal`
Expected: All tests pass

- [ ] **Step 4: Run existing unit tests to verify no regressions**

Run: `dotnet test tests/PayrollService.UnitTests/ && dotnet test tests/TransferService.UnitTests/`
Expected: All 32 + 27 tests pass

- [ ] **Step 5: Verify full solution builds**

Run: `dotnet build PayrollService.sln`
Expected: Build succeeded with no errors
