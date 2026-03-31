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
