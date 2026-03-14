using System.Text.Json;
using ListenerApi.Data.Services;
using MassTransit;

namespace ListenerApi.Consumers;

/// <summary>
/// Consumes messages from the employee-events Kafka topic.
/// Messages are CloudEvent-wrapped JSON (from PayrollService's Dapr outbox).
/// Format: { "type": "...", "source": "...", "data": "&lt;stringified entity JSON&gt;" }
/// OR the entity may be the raw value (non-CloudEvent format).
/// Handles both formats since the transition from Dapr may overlap.
/// </summary>
public class EmployeeEventConsumer : IConsumer<EmployeeEventMessage>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EmployeeEventConsumer> _logger;

    public EmployeeEventConsumer(
        IServiceProvider serviceProvider,
        ILogger<EmployeeEventConsumer> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<EmployeeEventMessage> context)
    {
        var body = context.Message.Value;

        if (string.IsNullOrWhiteSpace(body))
        {
            _logger.LogWarning("Received empty employee event message, skipping");
            return;
        }

        _logger.LogInformation("Received employee event via MassTransit, body length={Length}", body.Length);

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            EmployeeEventPayload? eventData = null;
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                if (dataElement.ValueKind == JsonValueKind.String)
                {
                    // data is stringified JSON (Dapr bug #8130) — parse the string
                    var dataString = dataElement.GetString();
                    if (!string.IsNullOrEmpty(dataString))
                        eventData = JsonSerializer.Deserialize<EmployeeEventPayload>(dataString, options);
                }
                else
                {
                    // data is a proper JSON object
                    eventData = dataElement.Deserialize<EmployeeEventPayload>(options);
                }
            }
            else
            {
                // No CloudEvent wrapper — try direct deserialization
                eventData = doc.RootElement.Deserialize<EmployeeEventPayload>(options);
            }

            if (eventData == null)
            {
                _logger.LogWarning("Failed to deserialize employee event");
                return;
            }

            var (employeeId, eventId, eventType, _) = eventData.ResolveEventInfo();
            _logger.LogInformation("Processing employee event: {EventType} {EventId} for {EmployeeId}",
                eventType, eventId, employeeId);

            using var scope = _serviceProvider.CreateScope();
            var eventProcessor = scope.ServiceProvider.GetRequiredService<EventProcessor>();
            await eventProcessor.ProcessEmployeeEventAsync(eventData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing employee event, body={Body}", body);
            throw; // Let MassTransit handle retry/error policy
        }
    }
}
