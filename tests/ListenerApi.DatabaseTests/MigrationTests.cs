// tests/ListenerApi.DatabaseTests/MigrationTests.cs
using ListenerApi.Data.DbContext;
using ListenerApi.DatabaseTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ListenerApi.DatabaseTests;

[Collection("ListenerMySql")]
public class MigrationTests
{
    private readonly MySqlFixture _fixture;

    public MigrationTests(MySqlFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AllMigrations_ApplyCleanly()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();

        var pending = await dbContext.Database.GetPendingMigrationsAsync();
        pending.Should().BeEmpty("all migrations should have been applied by the fixture");
    }
}
