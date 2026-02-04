using Microsoft.Extensions.DependencyInjection;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Events;
using PayrollService.Infrastructure.Persistence;
using PayrollService.Infrastructure.Repositories;
using PayrollService.Infrastructure.Seeding;

namespace PayrollService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString, string databaseName)
    {
        // Register MongoDB context
        services.AddSingleton(sp => new MongoDbContext(connectionString, databaseName));

        // Register repositories
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<ITimeEntryRepository, TimeEntryRepository>();
        services.AddScoped<ITaxInformationRepository, TaxInformationRepository>();
        services.AddScoped<IDeductionRepository, DeductionRepository>();

        // Register event publisher and unit of work
        services.AddScoped<IEventPublisher, DaprEventPublisher>();
        services.AddScoped<IUnitOfWork, TransactionalUnitOfWork>();

        // Register data seeder
        services.AddScoped<DataSeeder>();

        return services;
    }
}
