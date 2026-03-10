using TransferService.Domain.Common;
using TransferService.Domain.Enums;
using TransferService.Domain.Events;

namespace TransferService.Domain.Entities;

public class Transfer : Entity
{
    public Guid EmployeeId { get; private set; }
    public decimal Amount { get; private set; }
    public long PayPeriodNumber { get; private set; }
    public TransferStatus Status { get; private set; }
    public Guid BankAccountId { get; private set; }
    public DateTime InitiatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? FailureReason { get; private set; }
    public string? ExternalReferenceId { get; private set; }
    public decimal? CurrentBalance { get; private set; }

    private Transfer() { }

    public static Transfer Create(
        Guid employeeId,
        decimal amount,
        long payPeriodNumber,
        Guid bankAccountId)
    {
        if (amount <= 0)
            throw new ArgumentException("Transfer amount must be positive.", nameof(amount));

        var transfer = new Transfer
        {
            EmployeeId = employeeId,
            Amount = amount,
            PayPeriodNumber = payPeriodNumber,
            BankAccountId = bankAccountId,
            Status = TransferStatus.Initiated,
            InitiatedAt = DateTime.UtcNow
        };

        transfer.AddDomainEvent(new TransferInitiatedEvent(
            transfer.Id, employeeId, amount, payPeriodNumber, bankAccountId));
        return transfer;
    }

    public void MarkProcessing()
    {
        Status = TransferStatus.Processing;
        SetUpdated();
        AddDomainEvent(new TransferProcessingEvent(Id, EmployeeId, Amount, PayPeriodNumber));
    }

    public void MarkCompleted(string externalReferenceId)
    {
        Status = TransferStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        ExternalReferenceId = externalReferenceId;
        SetUpdated();
        AddDomainEvent(new TransferCompletedEvent(Id, EmployeeId, Amount, PayPeriodNumber, externalReferenceId));
    }

    public void MarkAwaitingConfirmation(decimal currentBalance)
    {
        Status = TransferStatus.AwaitingConfirmation;
        CurrentBalance = currentBalance;
        SetUpdated();
        AddDomainEvent(new TransferBalanceChangedEvent(Id, EmployeeId, Amount, currentBalance, PayPeriodNumber));
    }

    public void MarkFailed(string reason)
    {
        Status = TransferStatus.Failed;
        FailureReason = reason;
        SetUpdated();
        AddDomainEvent(new TransferFailedEvent(Id, EmployeeId, Amount, PayPeriodNumber, reason));
    }
}
