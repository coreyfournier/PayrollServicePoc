using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Orleans;
using PayrollService.Infrastructure.Orleans.Events;
using PayrollService.Infrastructure.Orleans.Repositories;
using PayrollService.Infrastructure.Seeding;

namespace PayrollService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        string kafkaBrokers = "localhost:9092")
    {
        // Register Kafka event publisher as singleton
        services.AddSingleton<IKafkaEventPublisher>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<KafkaEventPublisher>>();
            return new KafkaEventPublisher(kafkaBrokers, logger);
        });

        // Register Orleans repositories
        services.AddScoped<IEmployeeRepository, OrleansEmployeeRepository>();
        services.AddScoped<ITimeEntryRepository, OrleansTimeEntryRepository>();
        services.AddScoped<ITaxInformationRepository, OrleansTaxInformationRepository>();
        services.AddScoped<IDeductionRepository, OrleansDeductionRepository>();

        // Register Orleans unit of work (events published by grains)
        services.AddScoped<IUnitOfWork, OrleansUnitOfWork>();

        // Register data seeder as hosted service
        services.AddScoped<DataSeeder>();
        services.AddHostedService<DataSeederHostedService>();

        return services;
    }
}
