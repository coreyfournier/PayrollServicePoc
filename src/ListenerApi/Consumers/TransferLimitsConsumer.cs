using System.Text.Json;
using ListenerApi.Data.Services;
using MassTransit;

namespace ListenerApi.Consumers;

public class TransferLimitsConsumer : IConsumer<TransferLimitsMessage>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TransferLimitsConsumer> _logger;

    public TransferLimitsConsumer(
        IServiceProvider serviceProvider,
        ILogger<TransferLimitsConsumer> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TransferLimitsMessage> context)
    {
        var body = context.Message.Value;

        if (string.IsNullOrWhiteSpace(body))
        {
            _logger.LogWarning("Received empty transfer limits message, skipping");
            return;
        }

        _logger.LogInformation("Received transfer limits event, body length={Length}", body.Length);

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var payload = JsonSerializer.Deserialize<TransferLimitsPayload>(body, options);

            if (payload == null || string.IsNullOrEmpty(payload.EmployeeId))
            {
                _logger.LogWarning("Failed to deserialize transfer limits event");
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var eventProcessor = scope.ServiceProvider.GetRequiredService<EventProcessor>();
            await eventProcessor.ProcessTransferLimitsEventAsync(payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing transfer limits event, body={Body}", body);
            throw;
        }
    }
}
