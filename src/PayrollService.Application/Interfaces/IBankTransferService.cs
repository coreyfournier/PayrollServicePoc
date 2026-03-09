namespace PayrollService.Application.Interfaces;

public record BankTransferResult(bool Success, string? ExternalReferenceId, string? ErrorMessage);

public interface IBankTransferService
{
    Task<BankTransferResult> ExecuteTransferAsync(
        Guid transferId,
        decimal amount,
        string routingNumber,
        string accountNumberMasked,
        CancellationToken cancellationToken = default);
}
