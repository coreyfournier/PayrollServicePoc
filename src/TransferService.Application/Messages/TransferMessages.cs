namespace TransferService.Application.Messages;

// Commands
public record InitiateTransferMessage(Guid TransferId, Guid EmployeeId, decimal Amount, long PayPeriodNumber, Guid BankAccountId);
public record AcceptBalanceMessage(Guid TransferId, Guid EmployeeId, bool Accepted);

// Scheduled messages for saga
public record ConfirmationTimedOut(Guid TransferId);
public record RetryBankTransfer(Guid TransferId);
