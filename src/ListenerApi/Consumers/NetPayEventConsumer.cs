using System.Text.Json;
using ListenerApi.Data.Services;
using MassTransit;

namespace ListenerApi.Consumers;

/// <summary>
/// Consumes messages from the employee-net-pay Kafka topic.
/// Messages are raw JSON produced by the Java NetPayProcessor (not CloudEvent-wrapped),
/// but may also arrive as CloudEvents if routed through Dapr.
/// Handles both formats for transition safety.
/// </summary>
public class NetPayEventConsumer : IConsumer<NetPayEventMessage>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NetPayEventConsumer> _logger;

    public NetPayEventConsumer(
        IServiceProvider serviceProvider,
        ILogger<NetPayEventConsumer> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<NetPayEventMessage> context)
    {
        var body = context.Message.Value;

        if (string.IsNullOrWhiteSpace(body))
        {
            _logger.LogWarning("Received empty net pay event message, skipping");
            return;
        }

        _logger.LogInformation("Received net pay event via MassTransit, body length={Length}", body.Length);

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            NetPayEventPayload? eventData = null;
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                if (dataElement.ValueKind == JsonValueKind.String)
                {
                    var dataString = dataElement.GetString();
                    if (!string.IsNullOrEmpty(dataString))
                        eventData = JsonSerializer.Deserialize<NetPayEventPayload>(dataString, options);
                }
                else
                {
                    eventData = dataElement.Deserialize<NetPayEventPayload>(options);
                }
            }
            else
            {
                eventData = doc.RootElement.Deserialize<NetPayEventPayload>(options);
            }

            if (eventData == null)
            {
                _logger.LogWarning("Failed to deserialize net pay event");
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var eventProcessor = scope.ServiceProvider.GetRequiredService<EventProcessor>();
            await eventProcessor.ProcessNetPayEventAsync(eventData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing net pay event, body={Body}", body);
            throw;
        }
    }
}
