using TransferService.Domain.Common;

namespace TransferService.Domain.Events;

public class TransferInitiatedEvent : DomainEvent
{
    public override string EventType => "transfer.initiated";
    public Guid TransferId { get; }
    public Guid EmployeeId { get; }
    public decimal Amount { get; }
    public long PayPeriodNumber { get; }
    public Guid BankAccountId { get; }

    public TransferInitiatedEvent(Guid transferId, Guid employeeId, decimal amount, long payPeriodNumber, Guid bankAccountId)
    {
        TransferId = transferId;
        EmployeeId = employeeId;
        Amount = amount;
        PayPeriodNumber = payPeriodNumber;
        BankAccountId = bankAccountId;
    }
}

public class TransferProcessingEvent : DomainEvent
{
    public override string EventType => "transfer.processing";
    public Guid TransferId { get; }
    public Guid EmployeeId { get; }
    public decimal Amount { get; }
    public long PayPeriodNumber { get; }

    public TransferProcessingEvent(Guid transferId, Guid employeeId, decimal amount, long payPeriodNumber)
    {
        TransferId = transferId;
        EmployeeId = employeeId;
        Amount = amount;
        PayPeriodNumber = payPeriodNumber;
    }
}

public class TransferCompletedEvent : DomainEvent
{
    public override string EventType => "transfer.completed";
    public Guid TransferId { get; }
    public Guid EmployeeId { get; }
    public decimal Amount { get; }
    public long PayPeriodNumber { get; }
    public string ExternalReferenceId { get; }

    public TransferCompletedEvent(Guid transferId, Guid employeeId, decimal amount, long payPeriodNumber, string externalReferenceId)
    {
        TransferId = transferId;
        EmployeeId = employeeId;
        Amount = amount;
        PayPeriodNumber = payPeriodNumber;
        ExternalReferenceId = externalReferenceId;
    }
}

public class TransferBalanceChangedEvent : DomainEvent
{
    public override string EventType => "transfer.balance_changed";
    public Guid TransferId { get; }
    public Guid EmployeeId { get; }
    public decimal Amount { get; }
    public decimal CurrentBalance { get; }
    public long PayPeriodNumber { get; }

    public TransferBalanceChangedEvent(Guid transferId, Guid employeeId, decimal amount, decimal currentBalance, long payPeriodNumber)
    {
        TransferId = transferId;
        EmployeeId = employeeId;
        Amount = amount;
        CurrentBalance = currentBalance;
        PayPeriodNumber = payPeriodNumber;
    }
}

public class TransferFailedEvent : DomainEvent
{
    public override string EventType => "transfer.failed";
    public Guid TransferId { get; }
    public Guid EmployeeId { get; }
    public decimal Amount { get; }
    public long PayPeriodNumber { get; }
    public string Reason { get; }

    public TransferFailedEvent(Guid transferId, Guid employeeId, decimal amount, long payPeriodNumber, string reason)
    {
        TransferId = transferId;
        EmployeeId = employeeId;
        Amount = amount;
        PayPeriodNumber = payPeriodNumber;
        Reason = reason;
    }
}
