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
