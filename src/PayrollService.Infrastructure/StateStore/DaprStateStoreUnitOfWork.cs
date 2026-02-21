using System.Text.Json;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Common;

namespace PayrollService.Infrastructure.StateStore;

public class DaprStateStoreUnitOfWork : IUnitOfWork
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<DaprStateStoreUnitOfWork> _logger;
    private const string StateStoreName = "statestore-mongodb";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public DaprStateStoreUnitOfWork(DaprClient daprClient, ILogger<DaprStateStoreUnitOfWork> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        var domainEvents = entity.DomainEvents.ToList();

        // Step 1: Dapr state store transaction (atomic entity + outbox) — SOURCE OF TRUTH
        if (domainEvents.Count > 0)
        {
            await PublishEventsWithOutbox(entity, domainEvents, cancellationToken);
        }

        entity.ClearDomainEvents();

        // Step 2: MongoDB collection write — BEST-EFFORT READ MODEL
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MongoDB read-model write failed for {EntityType} {EntityId}. Entity is safely stored in Dapr state store.",
                entity.GetType().Name, entity.Id);

            // Entity is already persisted in Dapr state store; return it directly
            if (entity is T typedEntity)
            {
                return typedEntity;
            }

            return default!;
        }
    }

    public async Task ExecuteAsync(Func<Task> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        var domainEvents = entity.DomainEvents.ToList();

        // Step 1: Dapr state store transaction (atomic entity + outbox) — SOURCE OF TRUTH
        if (domainEvents.Count > 0)
        {
            await PublishEventsWithOutbox(entity, domainEvents, cancellationToken);
        }

        entity.ClearDomainEvents();

        // Step 2: MongoDB collection write — BEST-EFFORT READ MODEL
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
        var requests = new List<StateTransactionRequest>();

        // Serialize entity as proper JSON object (not string)
        var entityBytes = SerializeAsJsonObject(entity);
        requests.Add(new StateTransactionRequest(stateKey, entityBytes, StateOperationType.Upsert));

        foreach (var domainEvent in domainEvents)
        {
            var topicName = GetTopicName(domainEvent.EventType);

            // Serialize event as proper JSON object (not string) for Kafka payload
            var eventBytes = SerializeAsJsonObject(domainEvent);

            // Metadata for outbox entry - cloudevent fields
            // NOTE: outbox.projection is intentionally NOT set. Without projection,
            // Dapr publishes the entity state (which includes DomainEvents array,
            // Id, UpdatedAt, PayRate, etc.) — matching the format expected by
            // ksqlDB ($.DomainEvents[0].EventType) and NetPayProcessor.
            var outboxMetadata = new Dictionary<string, string>
            {
                ["cloudevent.source"] = "payroll-api",
                ["cloudevent.type"] = domainEvent.EventType,
                ["cloudevent.datacontenttype"] = "application/json",
                ["datacontenttype"] = "application/json",
                ["contenttype"] = "application/json",
            };

            requests.Add(new StateTransactionRequest(
                $"{stateKey}-event-{domainEvent.EventId}",
                eventBytes,
                StateOperationType.Upsert,
                metadata: outboxMetadata));
        }

        // CloudEvent metadata at transaction level
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

        //return JsonSerializer.SerializeToUtf8Bytes(value);
    }

    private static string GetStateKey(Entity entity)
    {
        var entityType = entity.GetType().Name.ToLowerInvariant();
        return StateKeyHelper.GetKey(entityType, entity.Id);
    }

    private static string GetTopicName(string eventType)
    {
        var prefix = eventType.Split('.')[0];
        return $"{prefix}-events";
    }
}
