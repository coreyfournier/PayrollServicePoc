using System.Text.Json;
using System.Text.Json.Serialization;
using MassTransit;
using Microsoft.Extensions.Logging;
using TransferService.Application.Interfaces;
using TransferService.Domain.Entities;

namespace TransferService.Infrastructure.Messaging;

public class TransferEventPublisher : ITransferEventPublisher
{
    private readonly ITopicProducer<string, string> _topicProducer;
    private readonly ILogger<TransferEventPublisher> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null, // PascalCase to match Dapr's format
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public TransferEventPublisher(
        ITopicProducer<string, string> topicProducer,
        ILogger<TransferEventPublisher> logger)
    {
        _topicProducer = topicProducer;
        _logger = logger;
    }

    public async Task PublishAsync(Transfer transfer, CancellationToken cancellationToken = default)
    {
        try
        {
            var entityJson = JsonSerializer.Serialize(transfer, JsonOptions);
            var cloudEvent = CloudEventWrapper.Create(entityJson);
            var message = JsonSerializer.Serialize(cloudEvent, JsonOptions);

            await _topicProducer.Produce(transfer.Id.ToString(), message, cancellationToken);

            _logger.LogInformation(
                "Published transfer event for {TransferId} with status {Status}",
                transfer.Id, transfer.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish transfer event for {TransferId}", transfer.Id);
            throw;
        }
    }
}
