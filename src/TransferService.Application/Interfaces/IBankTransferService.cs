namespace TransferService.Application.Interfaces;

public interface IBankTransferService
{
    Task<BankTransferResult> ExecuteTransferAsync(Guid transferId, decimal amount, Guid bankAccountId);
}

public record BankTransferResult(bool Success, string? ExternalReferenceId, string? ErrorMessage);
