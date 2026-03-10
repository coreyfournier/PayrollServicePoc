using TransferService.Domain.Entities;
using TransferService.Domain.Enums;
using TransferService.Domain.Events;

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
}
