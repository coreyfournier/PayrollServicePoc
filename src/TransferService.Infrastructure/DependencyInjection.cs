using Microsoft.Extensions.DependencyInjection;
using TransferService.Application.Interfaces;
using TransferService.Application.Services;
using TransferService.Domain.Repositories;
using TransferService.Infrastructure.ExternalServices;
using TransferService.Infrastructure.Messaging;
using TransferService.Infrastructure.Persistence;
using TransferService.Infrastructure.Repositories;

namespace TransferService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTransferInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string databaseName)
    {
        services.AddSingleton(sp => new TransferMongoDbContext(connectionString, databaseName));

        services.AddScoped<ITransferRepository, TransferRepository>();
        services.AddScoped<IEmployeeTransferLimitsRepository, EmployeeTransferLimitsRepository>();
        services.AddScoped<IUnitOfWork, MassTransitUnitOfWork>();
        services.AddScoped<IBankTransferService, SimulatedBankService>();
        services.AddScoped<ITransferValidationService, TransferValidationService>();
        services.AddScoped<ITransferEventPublisher, TransferEventPublisher>();
        services.AddScoped<ILimitsEventPublisher, LimitsEventPublisher>();

        services.AddHttpClient<IBalanceService, KsqlDbBalanceService>(client =>
        {
            client.BaseAddress = new Uri("http://ksqldb-server:8088");
        });

        return services;
    }
}
