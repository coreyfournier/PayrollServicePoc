using System.Text.Json;
using PayrollService.Domain.Common;

namespace PayrollService.Infrastructure.Messaging;

public static class CloudEventWrapper
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = null, // PascalCase to match Dapr outbox format
        WriteIndented = false
    };

    /// <summary>
    /// Wraps an entity in a CloudEvent envelope compatible with the existing ksqlDB pipeline.
    /// The data field is a STRINGIFIED JSON string (not a nested object) to match Dapr bug #8130 behavior.
    /// </summary>
    public static string Wrap(Entity entity)
    {
        // Serialize the entity to a JSON string (PascalCase properties, includes DomainEvents array)
        var entityJson = JsonSerializer.Serialize(entity, entity.GetType(), SerializeOptions);

        // Build the CloudEvent envelope with data as a stringified JSON value
        var cloudEvent = new
        {
            type = "com.dapr.event.sent",
            source = "payroll-api",
            data = entityJson
        };

        return JsonSerializer.Serialize(cloudEvent, SerializeOptions);
    }

    /// <summary>
    /// Determines the Kafka topic based on the first domain event's EventType prefix.
    /// e.g., "employee.created" -> "employee-events", "timeentry.clockedout" -> "timeentry-events"
    /// </summary>
    public static string GetTopicName(Entity entity)
    {
        var firstEvent = entity.DomainEvents.FirstOrDefault();
        if (firstEvent == null)
            throw new InvalidOperationException("Entity has no domain events to determine topic.");

        var prefix = firstEvent.EventType.Split('.')[0];
        return $"{prefix}-events";
    }
}
