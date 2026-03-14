using Microsoft.Extensions.DependencyInjection;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Messaging;
using PayrollService.Infrastructure.Persistence;
using PayrollService.Infrastructure.Repositories;

namespace PayrollService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string databaseName)
    {
        // Register MongoDB context
        services.AddSingleton(sp => new MongoDbContext(connectionString, databaseName));

        // Register MongoDB repositories
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<ITimeEntryRepository, TimeEntryRepository>();
        services.AddScoped<ITaxInformationRepository, TaxInformationRepository>();
        services.AddScoped<IDeductionRepository, DeductionRepository>();

        // Register MassTransit unit of work (publishes domain events to Kafka)
        services.AddScoped<IUnitOfWork, MassTransitUnitOfWork>();

        return services;
    }
}
