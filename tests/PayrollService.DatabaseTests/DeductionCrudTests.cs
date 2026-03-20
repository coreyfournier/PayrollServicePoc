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
