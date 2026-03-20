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

        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<TransferLimitsOptions>(opts =>
        {
            opts.MaxPerPayPeriod = 5;
            opts.MaxAmountPerPayPeriod = 10000m;
            opts.MaxPerDay = 1;
        });
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
