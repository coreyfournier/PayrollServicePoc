using System.Text.Json;
using Dapr.Client;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Common;

namespace PayrollService.Infrastructure.StateStore;

public class DaprStateStoreUnitOfWork : IUnitOfWork
{
    private readonly DaprClient _daprClient;
    private const string StateStoreName = "statestore-mongodb";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public DaprStateStoreUnitOfWork(DaprClient daprClient)
    {
        _daprClient = daprClient;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        var domainEvents = entity.DomainEvents.ToList();

        var result = await operation();

        if (domainEvents.Count > 0)
        {
            await PublishEventsWithOutbox(entity, domainEvents, cancellationToken);
        }

        entity.ClearDomainEvents();
        return result;
    }

    public async Task ExecuteAsync(Func<Task> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async () =>
        {
            await operation();
            return true;
        }, entity, cancellationToken);
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

            // Metadata for outbox projection - cloudevent fields without 'outbox.' prefix
            var outboxMetadata = new Dictionary<string, string>
            {
                ["cloudevent.source"] = "payroll-api",
                ["cloudevent.type"] = domainEvent.EventType,
                ["cloudevent.datacontenttype"] = "application/json",
                ["datacontenttype"] = "application/json",
                ["contenttype"] = "application/json",
                ["outbox.projection"] = "true"
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
