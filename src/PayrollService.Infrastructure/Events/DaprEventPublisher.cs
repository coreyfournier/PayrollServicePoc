using System.Text.Json;
using Dapr.Client;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Common;

namespace PayrollService.Infrastructure.Events;

public class DaprEventPublisher : IEventPublisher
{
    private readonly DaprClient _daprClient;
    private const string PubSubName = "kafka-pubsub";

    public DaprEventPublisher(DaprClient daprClient)
    {
        _daprClient = daprClient;
    }

    public async Task PublishAsync(DomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var topicName = GetTopicName(domainEvent.EventType);

        // Serialize with the actual runtime type to include all derived properties
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        var json = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), options);
        var eventData = JsonSerializer.Deserialize<object>(json);

        await _daprClient.PublishEventAsync(PubSubName, topicName, eventData, cancellationToken);
    }

    public async Task PublishAsync(IEnumerable<DomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            await PublishAsync(domainEvent, cancellationToken);
        }
    }

    private static string GetTopicName(string eventType)
    {
        // Convert event type like "employee.created" to topic name "employee-events"
        var prefix = eventType.Split('.')[0];
        return $"{prefix}-events";
    }
}
