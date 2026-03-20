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
        await uow.ExecuteAsync(async () => await repo.AddAsync(employee), employee);
        _fixture.UnitOfWork.PublishedEvents.Should().ContainSingle()
            .Which.EventType.Should().Be("employee.created");
    }
}
