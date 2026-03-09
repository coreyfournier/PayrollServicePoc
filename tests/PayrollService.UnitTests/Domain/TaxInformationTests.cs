using PayrollService.Domain.Entities;
using PayrollService.Domain.Events;

namespace PayrollService.UnitTests.Domain;

public class TaxInformationTests
{
    private readonly Guid _employeeId = Guid.NewGuid();

    [Fact]
    public void Create_ShouldSetPropertiesCorrectly()
    {
        var taxInfo = TaxInformation.Create(_employeeId, "Single", 1, 50m, "CA", "Single", 1, 25m);

        taxInfo.EmployeeId.Should().Be(_employeeId);
        taxInfo.FederalFilingStatus.Should().Be("Single");
        taxInfo.FederalAllowances.Should().Be(1);
        taxInfo.AdditionalFederalWithholding.Should().Be(50m);
        taxInfo.State.Should().Be("CA");
        taxInfo.StateFilingStatus.Should().Be("Single");
        taxInfo.StateAllowances.Should().Be(1);
        taxInfo.AdditionalStateWithholding.Should().Be(25m);
    }

    [Fact]
    public void Create_ShouldRaiseTaxInformationCreatedEvent()
    {
        var taxInfo = TaxInformation.Create(_employeeId, "Single", 1, 50m, "CA", "Single", 1, 25m);

        taxInfo.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TaxInformationCreatedEvent>();
    }

    [Fact]
    public void Update_ShouldModifyPropertiesAndRaiseEvent()
    {
        var taxInfo = TaxInformation.Create(_employeeId, "Single", 1, 50m, "CA", "Single", 1, 25m);
        taxInfo.ClearDomainEvents();

        taxInfo.Update("Married", 2, 100m, "NY", "Married", 2, 50m);

        taxInfo.FederalFilingStatus.Should().Be("Married");
        taxInfo.FederalAllowances.Should().Be(2);
        taxInfo.AdditionalFederalWithholding.Should().Be(100m);
        taxInfo.State.Should().Be("NY");
        taxInfo.StateFilingStatus.Should().Be("Married");
        taxInfo.StateAllowances.Should().Be(2);
        taxInfo.AdditionalStateWithholding.Should().Be(50m);
        taxInfo.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TaxInformationUpdatedEvent>();
    }
}
