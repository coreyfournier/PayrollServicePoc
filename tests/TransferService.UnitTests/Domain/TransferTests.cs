using TransferService.Domain.Entities;
using TransferService.Domain.Enums;
using TransferService.Domain.Events;
using TransferService.Domain.ValueObjects;

namespace TransferService.UnitTests.Domain;

public class TransferTests
{
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _bankAccountId = Guid.NewGuid();

    [Fact]
    public void Create_ShouldSetPropertiesAndStatusToInitiated()
    {
        var transfer = Transfer.Create(_employeeId, 500m, 55, _bankAccountId);

        transfer.EmployeeId.Should().Be(_employeeId);
        transfer.Amount.Should().Be(500m);
        transfer.PayPeriodNumber.Should().Be(55);
        transfer.BankAccountId.Should().Be(_bankAccountId);
        transfer.Status.Should().Be(TransferStatus.Initiated);
        transfer.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TransferInitiatedEvent>();
    }

    [Fact]
    public void Create_ShouldInitializeWorkflowSteps()
    {
        var transfer = Transfer.Create(_employeeId, 500m, 55, _bankAccountId);

        transfer.WorkflowSteps.Should().HaveCount(2);
        transfer.WorkflowSteps[0].Name.Should().Be(WorkflowStep.Names.Validation);
        transfer.WorkflowSteps[0].Status.Should().Be(WorkflowStep.Statuses.InProgress);
        transfer.WorkflowSteps[0].StartedAt.Should().NotBeNull();
        transfer.WorkflowSteps[1].Name.Should().Be(WorkflowStep.Names.BalanceCheck);
        transfer.WorkflowSteps[1].Status.Should().Be(WorkflowStep.Statuses.Pending);
    }

    [Fact]
    public void Create_WithZeroAmount_ShouldThrow()
    {
        var act = () => Transfer.Create(_employeeId, 0m, 55, _bankAccountId);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Transfer amount must be positive.*");
    }

    [Fact]
    public void Create_WithNegativeAmount_ShouldThrow()
    {
        var act = () => Transfer.Create(_employeeId, -100m, 55, _bankAccountId);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkProcessing_ShouldSetStatusAndRaiseEvent()
    {
        var transfer = Transfer.Create(_employeeId, 500m, 55, _bankAccountId);
        transfer.ClearDomainEvents();

        transfer.MarkProcessing();

        transfer.Status.Should().Be(TransferStatus.Processing);
        transfer.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TransferProcessingEvent>();
    }

    [Fact]
    public void MarkCompleted_ShouldSetStatusAndExternalReferenceId()
    {
        var transfer = Transfer.Create(_employeeId, 500m, 55, _bankAccountId);
        transfer.ClearDomainEvents();

        transfer.MarkCompleted("ext-ref-123");

        transfer.Status.Should().Be(TransferStatus.Completed);
        transfer.ExternalReferenceId.Should().Be("ext-ref-123");
        transfer.CompletedAt.Should().NotBeNull();
        transfer.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TransferCompletedEvent>();
    }

    [Fact]
    public void MarkAwaitingConfirmation_ShouldSetStatusAndBalance()
    {
        var transfer = Transfer.Create(_employeeId, 500m, 55, _bankAccountId);
        transfer.ClearDomainEvents();

        transfer.MarkAwaitingConfirmation(1500m);

        transfer.Status.Should().Be(TransferStatus.AwaitingConfirmation);
        transfer.CurrentBalance.Should().Be(1500m);
        transfer.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TransferBalanceChangedEvent>();
    }

    [Fact]
    public void MarkFailed_ShouldSetStatusAndFailureReason()
    {
        var transfer = Transfer.Create(_employeeId, 500m, 55, _bankAccountId);
        transfer.ClearDomainEvents();

        transfer.MarkFailed("Insufficient funds");

        transfer.Status.Should().Be(TransferStatus.Failed);
        transfer.FailureReason.Should().Be("Insufficient funds");
        transfer.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TransferFailedEvent>();
    }

    [Fact]
    public void CompleteWorkflowStep_ShouldUpdateStatusAndTimestamp()
    {
        var transfer = Transfer.Create(_employeeId, 500m, 55, _bankAccountId);

        transfer.CompleteWorkflowStep(WorkflowStep.Names.Validation, "All checks passed");

        var step = transfer.WorkflowSteps.Find(s => s.Name == WorkflowStep.Names.Validation)!;
        step.Status.Should().Be(WorkflowStep.Statuses.Completed);
        step.CompletedAt.Should().NotBeNull();
        step.Detail.Should().Be("All checks passed");
    }

    [Fact]
    public void FailWorkflowStep_ShouldUpdateStatusAndDetail()
    {
        var transfer = Transfer.Create(_employeeId, 500m, 55, _bankAccountId);

        transfer.FailWorkflowStep(WorkflowStep.Names.Validation, "Invalid bank account");

        var step = transfer.WorkflowSteps.Find(s => s.Name == WorkflowStep.Names.Validation)!;
        step.Status.Should().Be(WorkflowStep.Statuses.Failed);
        step.CompletedAt.Should().NotBeNull();
        step.Detail.Should().Be("Invalid bank account");
    }

    [Fact]
    public void StartWorkflowStep_ShouldSetInProgressAndTimestamp()
    {
        var transfer = Transfer.Create(_employeeId, 500m, 55, _bankAccountId);

        transfer.StartWorkflowStep(WorkflowStep.Names.BalanceCheck);

        var step = transfer.WorkflowSteps.Find(s => s.Name == WorkflowStep.Names.BalanceCheck)!;
        step.Status.Should().Be(WorkflowStep.Statuses.InProgress);
        step.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public void AddWorkflowStep_ShouldAppendToList()
    {
        var transfer = Transfer.Create(_employeeId, 500m, 55, _bankAccountId);

        transfer.AddWorkflowStep(WorkflowStep.Names.BankTransfer, WorkflowStep.Statuses.InProgress);

        transfer.WorkflowSteps.Should().HaveCount(3);
        var step = transfer.WorkflowSteps[2];
        step.Name.Should().Be(WorkflowStep.Names.BankTransfer);
        step.Status.Should().Be(WorkflowStep.Statuses.InProgress);
        step.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public void AddWorkflowStep_Pending_ShouldNotSetStartedAt()
    {
        var transfer = Transfer.Create(_employeeId, 500m, 55, _bankAccountId);

        transfer.AddWorkflowStep(WorkflowStep.Names.Complete, WorkflowStep.Statuses.Pending);

        var step = transfer.WorkflowSteps.Find(s => s.Name == WorkflowStep.Names.Complete)!;
        step.StartedAt.Should().BeNull();
    }

    [Fact]
    public void IncrementWorkflowStepRetry_ShouldIncrementRetryCount()
    {
        var transfer = Transfer.Create(_employeeId, 500m, 55, _bankAccountId);
        transfer.AddWorkflowStep(WorkflowStep.Names.BankTransfer, WorkflowStep.Statuses.InProgress);

        transfer.IncrementWorkflowStepRetry(WorkflowStep.Names.BankTransfer);
        transfer.IncrementWorkflowStepRetry(WorkflowStep.Names.BankTransfer);

        var step = transfer.WorkflowSteps.Find(s => s.Name == WorkflowStep.Names.BankTransfer)!;
        step.RetryCount.Should().Be(2);
    }

    [Fact]
    public void FullHappyPath_WorkflowSteps_ShouldProgressCorrectly()
    {
        var transfer = Transfer.Create(_employeeId, 500m, 55, _bankAccountId);

        // Validation passes
        transfer.CompleteWorkflowStep(WorkflowStep.Names.Validation);
        // Balance check passes
        transfer.CompleteWorkflowStep(WorkflowStep.Names.BalanceCheck);
        // Add remaining steps
        transfer.AddWorkflowStep(WorkflowStep.Names.BankTransfer, WorkflowStep.Statuses.InProgress);
        transfer.AddWorkflowStep(WorkflowStep.Names.Complete, WorkflowStep.Statuses.Pending);
        // Bank transfer completes
        transfer.CompleteWorkflowStep(WorkflowStep.Names.BankTransfer);
        transfer.CompleteWorkflowStep(WorkflowStep.Names.Complete);

        transfer.WorkflowSteps.Should().HaveCount(4);
        transfer.WorkflowSteps.Should().OnlyContain(s => s.Status == WorkflowStep.Statuses.Completed);
    }

    [Fact]
    public void InsufficientBalancePath_WorkflowSteps_ShouldIncludeAwaitingConfirmation()
    {
        var transfer = Transfer.Create(_employeeId, 500m, 55, _bankAccountId);

        // Validation passes
        transfer.CompleteWorkflowStep(WorkflowStep.Names.Validation);
        // Balance insufficient
        transfer.CompleteWorkflowStep(WorkflowStep.Names.BalanceCheck, "Balance $200.00 is less than transfer amount");
        transfer.AddWorkflowStep(WorkflowStep.Names.AwaitingConfirmation, WorkflowStep.Statuses.InProgress);
        transfer.AddWorkflowStep(WorkflowStep.Names.BankTransfer, WorkflowStep.Statuses.Pending);
        transfer.AddWorkflowStep(WorkflowStep.Names.Complete, WorkflowStep.Statuses.Pending);

        transfer.WorkflowSteps.Should().HaveCount(5);
        transfer.WorkflowSteps[2].Name.Should().Be(WorkflowStep.Names.AwaitingConfirmation);
        transfer.WorkflowSteps[2].Status.Should().Be(WorkflowStep.Statuses.InProgress);
    }
}
