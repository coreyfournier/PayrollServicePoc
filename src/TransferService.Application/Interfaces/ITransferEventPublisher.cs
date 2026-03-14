using TransferService.Domain.Entities;

namespace TransferService.Application.Interfaces;

public interface ITransferEventPublisher
{
    Task PublishAsync(Transfer transfer, CancellationToken cancellationToken = default);
}
