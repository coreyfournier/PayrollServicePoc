using Microsoft.Extensions.DependencyInjection;
using PayrollService.Application.Interfaces;
using PayrollService.DatabaseTests.TestDoubles;
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
