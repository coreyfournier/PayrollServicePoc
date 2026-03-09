using Microsoft.Extensions.DependencyInjection;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Events;
using PayrollService.Infrastructure.ExternalServices;
using PayrollService.Infrastructure.Persistence;
using PayrollService.Infrastructure.Repositories;
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

            // Transfer-specific registrations (separate state store for transfer-events topic)
            services.AddScoped<ITransferRepository, DaprTransferRepository>();
            services.AddScoped<IBankAccountRepository, DaprBankAccountRepository>();
            services.AddScoped<ITransferUnitOfWork, DaprTransferStateStoreUnitOfWork>();
            services.AddScoped<IBankTransferService, SimulatedBankService>();

            // Balance service (queries ksqlDB for real-time net pay)
            services.AddHttpClient<IBalanceService, KsqlDbBalanceService>(client =>
            {
                client.BaseAddress = new Uri("http://ksqldb-server:8088");
            });
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

        return services;
    }
}
