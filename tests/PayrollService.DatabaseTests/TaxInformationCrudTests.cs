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
