using PayrollService.Application.DTOs;

namespace PayrollService.Application.Interfaces;

public interface ITransferActorService
{
    Task<TransferDto> InitiateTransferAsync(
        Guid employeeId,
        decimal amount,
        long payPeriodNumber,
        Guid bankAccountId,
        CancellationToken cancellationToken = default);
}
