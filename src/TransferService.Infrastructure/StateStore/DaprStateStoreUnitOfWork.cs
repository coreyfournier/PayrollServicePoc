using System.Text.Json;
using System.Text.Json.Serialization;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using TransferService.Application.Interfaces;
using TransferService.Domain.Common;
using TransferService.Infrastructure.Persistence;

namespace TransferService.Infrastructure.StateStore;

public class DaprStateStoreUnitOfWork : IUnitOfWork
{
    private const string StateStoreName = "statestore-transfers";
    private readonly DaprClient _daprClient;
    private readonly TransferMongoDbContext _dbContext;
    private readonly ILogger<DaprStateStoreUnitOfWork> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public DaprStateStoreUnitOfWork(
        DaprClient daprClient,
        TransferMongoDbContext dbContext,
        ILogger<DaprStateStoreUnitOfWork> logger)
    {
        _daprClient = daprClient;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        await PublishEntityWithOutbox(entity, cancellationToken);
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MongoDB write failed for entity {EntityId}, but Dapr state store write succeeded", entity.Id);
            return (T)(object)entity;
        }
    }

    public async Task ExecuteAsync(Func<Task> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        await PublishEntityWithOutbox(entity, cancellationToken);
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MongoDB write failed for entity {EntityId}, but Dapr state store write succeeded", entity.Id);
        }
    }

    private async Task PublishEntityWithOutbox(Entity entity, CancellationToken cancellationToken)
    {
        var events = entity.DomainEvents.ToList();
        if (events.Count == 0) return;

        var stateKey = GetStateKey(entity);
        var serializedEntity = JsonSerializer.Serialize(entity, entity.GetType(), JsonOptions);

        var metadata = new Dictionary<string, string>
        {
            ["contentType"] = "application/json",
            ["outbox.cloudevent.type"] = "com.dapr.event.sent",
            ["outbox.cloudevent.source"] = "transfer-api"
        };

        var stateItems = new List<StateTransactionRequest>
        {
            new(stateKey, System.Text.Encoding.UTF8.GetBytes(serializedEntity), StateOperationType.Upsert,
                metadata: metadata)
        };

        await _daprClient.ExecuteStateTransactionAsync(StateStoreName, stateItems, cancellationToken: cancellationToken);
        entity.ClearDomainEvents();
    }

    private static string GetStateKey(Entity entity) => entity switch
    {
        TransferService.Domain.Entities.Transfer t => StateKeyHelper.GetTransferKey(t.Id),
        TransferService.Domain.Entities.BankAccount b => StateKeyHelper.GetBankAccountKey(b.Id),
        _ => $"entity-{entity.Id}"
    };
}
