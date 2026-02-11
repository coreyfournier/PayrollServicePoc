using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PayrollService.Infrastructure.Seeding;

public class DataSeederHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DataSeederHostedService> _logger;

    public DataSeederHostedService(
        IServiceProvider serviceProvider,
        ILogger<DataSeederHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit for Orleans silo to fully start
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();

            _logger.LogInformation("Starting data seeding...");
            await seeder.SeedAsync();
            _logger.LogInformation("Data seeding completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during data seeding");
        }
    }
}
