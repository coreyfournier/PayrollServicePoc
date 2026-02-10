using Microsoft.Extensions.DependencyInjection;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Events;
using PayrollService.Infrastructure.Persistence;
using PayrollService.Infrastructure.Repositories;
using PayrollService.Infrastructure.Seeding;
using PayrollService.Infrastructure.StateStore;

namespace PayrollService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        bool useDaprOutbox = false)
    {
        // Register MongoDB context
        services.AddSingleton(sp => new MongoDbContext(connectionString, databaseName));

        if (useDaprOutbox)
        {
            // Register Dapr hybrid repositories (writes via Dapr outbox, reads via MongoDB)
            services.AddScoped<IEmployeeRepository, DaprEmployeeRepository>();
            services.AddScoped<ITimeEntryRepository, DaprTimeEntryRepository>();
            services.AddScoped<ITaxInformationRepository, DaprTaxInformationRepository>();
            services.AddScoped<IDeductionRepository, DaprDeductionRepository>();

            // Register Dapr state store unit of work (uses native outbox pattern)
            services.AddScoped<IUnitOfWork, DaprStateStoreUnitOfWork>();
        }
        else
        {
            // Register legacy MongoDB-only repositories
            services.AddScoped<IEmployeeRepository, EmployeeRepository>();
            services.AddScoped<ITimeEntryRepository, TimeEntryRepository>();
            services.AddScoped<ITaxInformationRepository, TaxInformationRepository>();
            services.AddScoped<IDeductionRepository, DeductionRepository>();

            // Register legacy event publisher and unit of work
            services.AddScoped<IEventPublisher, DaprEventPublisher>();
            services.AddScoped<IUnitOfWork, TransactionalUnitOfWork>();
        }

        // Register data seeder
        services.AddScoped<DataSeeder>();

        return services;
    }
}
