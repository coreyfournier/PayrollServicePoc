using System.Text.Json;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Common;

namespace PayrollService.Infrastructure.StateStore;

/// <summary>
/// Unit of work targeting the transfer-specific Dapr state store.
/// Publishes transfer events to the transfer-events Kafka topic via outbox.
/// </summary>
public class DaprTransferStateStoreUnitOfWork : ITransferUnitOfWork
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<DaprTransferStateStoreUnitOfWork> _logger;
    private const string StateStoreName = "statestore-transfers";

    public DaprTransferStateStoreUnitOfWork(DaprClient daprClient, ILogger<DaprTransferStateStoreUnitOfWork> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        var domainEvents = entity.DomainEvents.ToList();

        if (domainEvents.Count > 0)
        {
            await PublishEventsWithOutbox(entity, domainEvents, cancellationToken);
        }

        entity.ClearDomainEvents();

        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MongoDB read-model write failed for {EntityType} {EntityId}. Entity is safely stored in Dapr state store.",
                entity.GetType().Name, entity.Id);

            if (entity is T typedEntity)
                return typedEntity;

            return default!;
        }
    }

    public async Task ExecuteAsync(Func<Task> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        var domainEvents = entity.DomainEvents.ToList();

        if (domainEvents.Count > 0)
        {
            await PublishEventsWithOutbox(entity, domainEvents, cancellationToken);
        }

        entity.ClearDomainEvents();

        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MongoDB read-model write failed for {EntityType} {EntityId}. Entity is safely stored in Dapr state store.",
                entity.GetType().Name, entity.Id);
        }
    }

    private async Task PublishEventsWithOutbox(Entity entity, List<DomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        var stateKey = GetStateKey(entity);

        var entityBytes = SerializeAsJsonObject(entity);
        var requests = new List<StateTransactionRequest>
        {
            new(stateKey, entityBytes, StateOperationType.Upsert)
        };

        var transactionMetadata = new Dictionary<string, string>
        {
            ["cloudevent.source"] = "payroll-api",
            ["cloudevent.type"] = domainEvents.First().EventType,
            ["cloudevent.datacontenttype"] = "application/json",
            ["datacontenttype"] = "application/json",
            ["contenttype"] = "application/json",
        };

        await _daprClient.ExecuteStateTransactionAsync(StateStoreName, requests, metadata: transactionMetadata, cancellationToken: cancellationToken);
    }

    private static byte[] SerializeAsJsonObject(object value)
    {
        var jsonString = JsonSerializer.Serialize(value);
        return System.Text.Encoding.UTF8.GetBytes(jsonString);
    }

    private static string GetStateKey(Entity entity)
    {
        var entityType = entity.GetType().Name.ToLowerInvariant();
        return StateKeyHelper.GetKey(entityType, entity.Id);
    }
}
