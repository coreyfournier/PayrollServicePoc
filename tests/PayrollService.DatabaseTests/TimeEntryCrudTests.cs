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
        ((double)retrieved.HoursWorked).Should().BeApproximately(8, 0.01);
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
        ((double)retrieved!.HoursWorked).Should().BeApproximately(6, 0.01);
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
