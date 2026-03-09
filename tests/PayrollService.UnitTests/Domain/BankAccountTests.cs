using PayrollService.Domain.Entities;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Events;

namespace PayrollService.UnitTests.Domain;

public class BankAccountTests
{
    private readonly Guid _employeeId = Guid.NewGuid();

    [Fact]
    public void Create_ShouldSetPropertiesCorrectly()
    {
        var account = BankAccount.Create(_employeeId, "Chase", "****1234", "021000021", BankAccountType.Checking);

        account.EmployeeId.Should().Be(_employeeId);
        account.BankName.Should().Be("Chase");
        account.AccountNumberMasked.Should().Be("****1234");
        account.RoutingNumber.Should().Be("021000021");
        account.AccountType.Should().Be(BankAccountType.Checking);
        account.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_ShouldRaiseBankAccountCreatedEvent()
    {
        var account = BankAccount.Create(_employeeId, "Chase", "****1234", "021000021", BankAccountType.Checking);

        account.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<BankAccountCreatedEvent>();
    }

    [Fact]
    public void Update_ShouldModifyPropertiesAndRaiseEvent()
    {
        var account = BankAccount.Create(_employeeId, "Chase", "****1234", "021000021", BankAccountType.Checking);
        account.ClearDomainEvents();

        account.Update("Wells Fargo", "****5678", "121000248", BankAccountType.Savings);

        account.BankName.Should().Be("Wells Fargo");
        account.AccountNumberMasked.Should().Be("****5678");
        account.RoutingNumber.Should().Be("121000248");
        account.AccountType.Should().Be(BankAccountType.Savings);
        account.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<BankAccountUpdatedEvent>();
    }

    [Fact]
    public void Deactivate_ShouldSetIsActiveFalseAndRaiseEvent()
    {
        var account = BankAccount.Create(_employeeId, "Chase", "****1234", "021000021", BankAccountType.Checking);
        account.ClearDomainEvents();

        account.Deactivate();

        account.IsActive.Should().BeFalse();
        account.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<BankAccountDeactivatedEvent>();
    }
}
