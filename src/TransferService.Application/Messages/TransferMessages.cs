namespace TransferService.Application.Messages;

// Commands (sent to initiate actions)
public record InitiateTransferMessage(Guid TransferId, Guid EmployeeId, decimal Amount, long PayPeriodNumber, Guid BankAccountId);
public record AcceptBalanceMessage(Guid TransferId, Guid EmployeeId, bool Accepted);

// Events (published when state changes)
public record TransferValidated(Guid TransferId);
public record TransferValidationFailed(Guid TransferId, string Reason);
public record BalanceVerified(Guid TransferId, decimal CurrentBalance);
public record BalanceInsufficient(Guid TransferId, decimal CurrentBalance);
public record BankTransferCompleted(Guid TransferId, string ExternalReferenceId);
public record BankTransferFailed(Guid TransferId, string Reason);
public record ConfirmationTimedOut(Guid TransferId);
public record RetryBankTransfer(Guid TransferId);
