using TransferService.Domain.Entities;

namespace TransferService.Application.Interfaces;

public interface ITransferEventPublisher
{
    Task PublishAsync(Transfer transfer, CancellationToken cancellationToken = default);
    Task PublishRejectionAsync(Guid transferId, Guid employeeId, decimal amount, long payPeriodNumber,
        Guid bankAccountId, string failureReason, CancellationToken cancellationToken = default);
}
