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
