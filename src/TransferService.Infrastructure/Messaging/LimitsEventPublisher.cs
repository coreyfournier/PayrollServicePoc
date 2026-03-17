using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using TransferService.Application.Interfaces;

namespace TransferService.Infrastructure.Messaging;

public class LimitsEventPublisher : ILimitsEventPublisher
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<LimitsEventPublisher> _logger;
    private const string TopicName = "transfer-limits";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public LimitsEventPublisher(
        IProducer<string, string> producer,
        ILogger<LimitsEventPublisher> logger)
    {
        _producer = producer;
        _logger = logger;
    }

    public async Task PublishAsync(Guid employeeId, int maxPerPayPeriod, decimal maxAmountPerPayPeriod, int maxPerDay, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                EmployeeId = employeeId.ToString(),
                MaxPerPayPeriod = maxPerPayPeriod,
                MaxAmountPerPayPeriod = (double)maxAmountPerPayPeriod,
                MaxPerDay = maxPerDay
            };

            var messageValue = JsonSerializer.Serialize(payload, JsonOptions);

            var message = new Message<string, string>
            {
                Key = employeeId.ToString(),
                Value = messageValue
            };

            var result = await _producer.ProduceAsync(TopicName, message, cancellationToken);

            _logger.LogInformation(
                "Published limits event for employee {EmployeeId} to partition {Partition}",
                employeeId, result.Partition.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish limits event for employee {EmployeeId}", employeeId);
            throw;
        }
    }
}
