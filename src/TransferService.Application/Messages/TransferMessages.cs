namespace TransferService.Application.Messages;

// Inbound events (from transfer-requests topic or API)
public record TransferRequested(Guid TransferId, Guid EmployeeId, decimal Amount, long PayPeriodNumber, Guid BankAccountId);
public record BalanceAccepted(Guid TransferId, Guid EmployeeId, bool Accepted);

// Scheduled events (in-memory bus) — parameterless ctor required by MassTransit scheduler Init<T>
public record ConfirmationTimedOut(Guid TransferId)
{
    public ConfirmationTimedOut() : this(Guid.Empty) { }
}
public record RetryBankTransfer(Guid TransferId)
{
    public RetryBankTransfer() : this(Guid.Empty) { }
}

// Workflow commands (saga → step consumers via RabbitMQ)
public record RunBalanceCheck(Guid TransferId, Guid EmployeeId, decimal Amount, long PayPeriodNumber);
public record RunFraudCheck(Guid TransferId);
public record RunBankTransfer(Guid TransferId, decimal Amount, Guid BankAccountId);

// Workflow step completion events (step consumers → saga via RabbitMQ)
public record BalanceCheckCompleted(Guid TransferId, bool Sufficient, decimal? CurrentBalance);
public record FraudCheckCompleted(Guid TransferId);
public record BankTransferCompleted(Guid TransferId, bool Success, string? ExternalReferenceId, string? ErrorMessage);

// Saga → Kafka bridge event (saga publishes to RabbitMQ, bridge consumer forwards to Kafka)
public record TransferUpdated(Guid TransferId);

// Limits → Kafka bridge event (published after custom limits CRUD, bridge consumer forwards to Kafka)
public record EmployeeLimitsUpdated(Guid EmployeeId);
