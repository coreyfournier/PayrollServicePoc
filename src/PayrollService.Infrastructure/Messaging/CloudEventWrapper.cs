using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// The data field is a proper nested JSON object.
    /// </summary>
    public static string Wrap(Entity entity)
    {
        // Serialize the entity to a JsonNode so it embeds as a nested JSON object (not a string)
        var entityNode = JsonSerializer.SerializeToNode(entity, entity.GetType(), SerializeOptions);

        var cloudEvent = new JsonObject
        {
            ["type"] = "com.dapr.event.sent",
            ["source"] = "payroll-api",
            ["data"] = entityNode
        };

        return cloudEvent.ToJsonString(SerializeOptions);
    }

    /// <summary>
    /// All domain events are published to the employee-events topic.
    /// The ksqlDB pipeline filters by DomainEvents[0].EventType to route
    /// employee, time entry, tax info, and deduction events to the correct streams.
    /// </summary>
    public static string GetTopicName(Entity entity)
    {
        return "employee-events";
    }
}
