using System.Text.Json;
using ListenerApi.Data.Services;
using MassTransit;

namespace ListenerApi.Consumers;

/// <summary>
/// Consumes messages from the transfer-events Kafka topic.
/// Messages are CloudEvent-wrapped JSON (from TransferService's Dapr outbox).
/// Handles both stringified data (Dapr bug #8130) and proper JSON objects.
/// </summary>
public class TransferEventConsumer : IConsumer<TransferEventMessage>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TransferEventConsumer> _logger;

    public TransferEventConsumer(
        IServiceProvider serviceProvider,
        ILogger<TransferEventConsumer> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TransferEventMessage> context)
    {
        var body = context.Message.Value;

        if (string.IsNullOrWhiteSpace(body))
        {
            _logger.LogWarning("Received empty transfer event message, skipping");
            return;
        }

        _logger.LogInformation("Received transfer event via MassTransit, body length={Length}", body.Length);

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            TransferEventPayload? eventData = null;
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                if (dataElement.ValueKind == JsonValueKind.String)
                {
                    var dataString = dataElement.GetString();
                    if (!string.IsNullOrEmpty(dataString))
                        eventData = JsonSerializer.Deserialize<TransferEventPayload>(dataString, options);
                }
                else
                {
                    eventData = dataElement.Deserialize<TransferEventPayload>(options);
                }
            }
            else
            {
                eventData = doc.RootElement.Deserialize<TransferEventPayload>(options);
            }

            if (eventData == null)
            {
                _logger.LogWarning("Failed to deserialize transfer event");
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var eventProcessor = scope.ServiceProvider.GetRequiredService<EventProcessor>();
            await eventProcessor.ProcessTransferEventAsync(eventData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing transfer event, body={Body}", body);
            throw;
        }
    }
}
