namespace TransferService.Application.Messages;

// Events
public record TransferRequested(Guid TransferId, Guid EmployeeId, decimal Amount, long PayPeriodNumber, Guid BankAccountId);
public record BalanceAccepted(Guid TransferId, Guid EmployeeId, bool Accepted);

// Scheduled messages for saga
public record ConfirmationTimedOut(Guid TransferId);
public record RetryBankTransfer(Guid TransferId);
