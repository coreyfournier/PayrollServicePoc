using PayrollService.Domain.Entities;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Events;

namespace PayrollService.UnitTests.Domain;

public class DeductionTests
{
    private readonly Guid _employeeId = Guid.NewGuid();

    [Fact]
    public void Create_ShouldSetPropertiesCorrectly()
    {
        var deduction = Deduction.Create(_employeeId, DeductionType.Health, "Health Insurance", 150m, false);

        deduction.EmployeeId.Should().Be(_employeeId);
        deduction.DeductionType.Should().Be(DeductionType.Health);
        deduction.Description.Should().Be("Health Insurance");
        deduction.Amount.Should().Be(150m);
        deduction.IsPercentage.Should().BeFalse();
        deduction.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_ShouldRaiseDeductionCreatedEvent()
    {
        var deduction = Deduction.Create(_employeeId, DeductionType.Retirement401k, "401k", 5m, true);

        deduction.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<DeductionCreatedEvent>();
    }

    [Fact]
    public void Update_ShouldModifyPropertiesAndRaiseEvent()
    {
        var deduction = Deduction.Create(_employeeId, DeductionType.Health, "Health Insurance", 150m, false);
        deduction.ClearDomainEvents();

        deduction.Update(DeductionType.Dental, "Dental Insurance", 75m, false);

        deduction.DeductionType.Should().Be(DeductionType.Dental);
        deduction.Description.Should().Be("Dental Insurance");
        deduction.Amount.Should().Be(75m);
        deduction.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<DeductionUpdatedEvent>();
    }

    [Fact]
    public void Deactivate_ShouldSetIsActiveFalseAndRaiseEvent()
    {
        var deduction = Deduction.Create(_employeeId, DeductionType.Health, "Health Insurance", 150m, false);
        deduction.ClearDomainEvents();

        deduction.Deactivate();

        deduction.IsActive.Should().BeFalse();
        deduction.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<DeductionDeactivatedEvent>();
    }
}
