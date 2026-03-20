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
