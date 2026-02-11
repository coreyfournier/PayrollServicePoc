using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace PayrollService.Infrastructure.Orleans.Events;

public interface IKafkaEventPublisher
{
    Task PublishAsync(string topic, object domainEvent);
}

public class KafkaEventPublisher : IKafkaEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaEventPublisher> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public KafkaEventPublisher(string brokers, ILogger<KafkaEventPublisher> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var config = new ProducerConfig
        {
            BootstrapServers = brokers,
            Acks = Acks.All,
            EnableIdempotence = true
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(string topic, object domainEvent)
    {
        try
        {
            var eventType = domainEvent.GetType().GetProperty("EventType")?.GetValue(domainEvent)?.ToString() ?? "unknown";
            var eventId = domainEvent.GetType().GetProperty("EventId")?.GetValue(domainEvent) as Guid? ?? Guid.NewGuid();
            var occurredOn = domainEvent.GetType().GetProperty("OccurredOn")?.GetValue(domainEvent) as DateTime? ?? DateTime.UtcNow;

            // Create CloudEvents format compatible with Dapr
            // data must be a JSON object, not a string, for Dapr to deserialize correctly
            var cloudEvent = new
            {
                data = domainEvent,
                datacontenttype = "application/json",
                id = eventId.ToString(),
                pubsubname = "kafka-pubsub-listener",
                source = "payroll-api",
                specversion = "1.0",
                time = occurredOn.ToString("O"),
                topic = topic,
                type = "com.dapr.event.sent"
            };

            var message = new Message<string, string>
            {
                Key = eventId.ToString(),
                Value = JsonSerializer.Serialize(cloudEvent, _jsonOptions)
            };

            var result = await _producer.ProduceAsync(topic, message);
            _logger.LogDebug("Published {EventType} event {EventId} to {Topic} at offset {Offset}",
                eventType, eventId, topic, result.Offset);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event to {Topic}", topic);
            throw;
        }
    }

    public void Dispose()
    {
        _producer?.Dispose();
    }
}
