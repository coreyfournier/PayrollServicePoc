using MassTransit;
using Microsoft.Extensions.Logging;
using TransferService.Application.Interfaces;
using TransferService.Application.Messages;
using TransferService.Domain.Repositories;

namespace TransferService.Api.Consumers;

public class TransferKafkaBridgeConsumer : IConsumer<TransferUpdated>
{
    private readonly ITransferRepository _repo;
    private readonly ITransferEventPublisher _publisher;
    private readonly ILogger<TransferKafkaBridgeConsumer> _logger;

    public TransferKafkaBridgeConsumer(
        ITransferRepository repo,
        ITransferEventPublisher publisher,
        ILogger<TransferKafkaBridgeConsumer> logger)
    {
        _repo = repo;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TransferUpdated> context)
    {
        var transferId = context.Message.TransferId;

        var transfer = await _repo.GetByIdAsync(transferId);
        if (transfer == null)
        {
            _logger.LogWarning("Transfer {TransferId} not found in MongoDB, skipping Kafka publish", transferId);
            return;
        }

        await _publisher.PublishAsync(transfer);
        _logger.LogInformation(
            "Bridged transfer {TransferId} (status={Status}) to Kafka",
            transferId, transfer.Status);
    }
}
