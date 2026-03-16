namespace TransferService.Application.Interfaces;

public record TransferValidationRequest(
    Guid EmployeeId,
    decimal Amount,
    long PayPeriodNumber,
    Guid BankAccountId,
    Guid? TransferId = null);

public record TransferValidationResult(
    bool CanTransfer,
    List<string> Reasons);

public interface ITransferValidationService
{
    Task<TransferValidationResult> ValidateAsync(TransferValidationRequest request, CancellationToken cancellationToken = default);
}
