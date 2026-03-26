using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using TransferService.Application.Interfaces;
using TransferService.Domain.Entities;

namespace TransferService.Infrastructure.Messaging;

public class TransferEventPublisher : ITransferEventPublisher
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<TransferEventPublisher> _logger;
    private const string TopicName = "transfer-events";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null, // PascalCase to match existing format
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public TransferEventPublisher(
        IProducer<string, string> producer,
        ILogger<TransferEventPublisher> logger)
    {
        _producer = producer;
        _logger = logger;
    }

    public async Task PublishAsync(Transfer transfer, CancellationToken cancellationToken = default)
    {
        try
        {
            var entityJsonElement = JsonSerializer.SerializeToElement(transfer, JsonOptions);
            var cloudEvent = CloudEventWrapper.Create(entityJsonElement);
            var messageValue = JsonSerializer.Serialize(cloudEvent, JsonOptions);

            var message = new Message<string, string>
            {
                Key = transfer.Id.ToString(),
                Value = messageValue
            };

            var result = await _producer.ProduceAsync(TopicName, message, cancellationToken);

            _logger.LogInformation(
                "Published transfer event for {TransferId} with status {Status} to partition {Partition}",
                transfer.Id, transfer.Status, result.Partition.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish transfer event for {TransferId}", transfer.Id);
            throw;
        }
    }

    public async Task PublishRejectionAsync(Guid transferId, Guid employeeId, decimal amount, long payPeriodNumber,
        Guid bankAccountId, string failureReason, CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var rejection = new
            {
                Id = transferId,
                EmployeeId = employeeId,
                Amount = amount,
                PayPeriodNumber = payPeriodNumber,
                Status = "Failed",
                BankAccountId = bankAccountId,
                InitiatedAt = now,
                CompletedAt = (DateTime?)null,
                FailureReason = failureReason,
                ExternalReferenceId = (string?)null,
                CurrentBalance = (decimal?)null,
                WorkflowSteps = Array.Empty<object>(),
                CreatedAt = now,
                UpdatedAt = now
            };

            var jsonElement = JsonSerializer.SerializeToElement(rejection, JsonOptions);
            var cloudEvent = CloudEventWrapper.Create(jsonElement);
            var messageValue = JsonSerializer.Serialize(cloudEvent, JsonOptions);

            var message = new Message<string, string>
            {
                Key = transferId.ToString(),
                Value = messageValue
            };

            var result = await _producer.ProduceAsync(TopicName, message, cancellationToken);

            _logger.LogInformation(
                "Published transfer rejection for {TransferId} to partition {Partition}: {Reason}",
                transferId, result.Partition.Value, failureReason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish transfer rejection for {TransferId}", transferId);
            throw;
        }
    }
}
