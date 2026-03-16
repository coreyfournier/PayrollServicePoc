using TransferService.Domain.Common;
using TransferService.Domain.Enums;
using TransferService.Domain.Events;
using TransferService.Domain.ValueObjects;

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
    public List<WorkflowStep> WorkflowSteps { get; private set; } = new();

    private Transfer() { }

    public static Transfer Create(
        Guid employeeId,
        decimal amount,
        long payPeriodNumber,
        Guid bankAccountId,
        Guid? transferId = null)
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

        if (transferId.HasValue)
            transfer.Id = transferId.Value;

        transfer.WorkflowSteps.Add(new WorkflowStep
        {
            Name = WorkflowStep.Names.Validation,
            Status = WorkflowStep.Statuses.InProgress,
            StartedAt = DateTime.UtcNow
        });
        transfer.WorkflowSteps.Add(new WorkflowStep
        {
            Name = WorkflowStep.Names.BalanceCheck,
            Status = WorkflowStep.Statuses.Pending
        });

        transfer.AddDomainEvent(new TransferInitiatedEvent(
            transfer.Id, employeeId, amount, payPeriodNumber, bankAccountId));
        return transfer;
    }

    public void StartWorkflowStep(string name)
    {
        var step = WorkflowSteps.Find(s => s.Name == name);
        if (step != null)
        {
            step.Status = WorkflowStep.Statuses.InProgress;
            step.StartedAt = DateTime.UtcNow;
        }
    }

    public void CompleteWorkflowStep(string name, string? detail = null)
    {
        var step = WorkflowSteps.Find(s => s.Name == name);
        if (step != null)
        {
            step.Status = WorkflowStep.Statuses.Completed;
            step.CompletedAt = DateTime.UtcNow;
            if (detail != null)
                step.Detail = detail;
        }
    }

    public void FailWorkflowStep(string name, string detail)
    {
        var step = WorkflowSteps.Find(s => s.Name == name);
        if (step != null)
        {
            step.Status = WorkflowStep.Statuses.Failed;
            step.CompletedAt = DateTime.UtcNow;
            step.Detail = detail;
        }
    }

    public void AddWorkflowStep(string name, string status)
    {
        WorkflowSteps.Add(new WorkflowStep
        {
            Name = name,
            Status = status,
            StartedAt = status == WorkflowStep.Statuses.InProgress ? DateTime.UtcNow : null
        });
    }

    public void IncrementWorkflowStepRetry(string name)
    {
        var step = WorkflowSteps.Find(s => s.Name == name);
        if (step != null)
            step.RetryCount++;
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
