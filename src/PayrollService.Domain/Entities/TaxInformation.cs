using PayrollService.Domain.Common;
using PayrollService.Domain.Events;

namespace PayrollService.Domain.Entities;

public class TaxInformation : Entity
{
    public Guid EmployeeId { get; private set; }
    public string FederalFilingStatus { get; private set; } = string.Empty;
    public int FederalAllowances { get; private set; }
    public decimal AdditionalFederalWithholding { get; private set; }
    public string State { get; private set; } = string.Empty;
    public string StateFilingStatus { get; private set; } = string.Empty;
    public int StateAllowances { get; private set; }
    public decimal AdditionalStateWithholding { get; private set; }

    private TaxInformation() { }

    public static TaxInformation Create(
        Guid employeeId,
        string federalFilingStatus,
        int federalAllowances,
        decimal additionalFederalWithholding,
        string state,
        string stateFilingStatus,
        int stateAllowances,
        decimal additionalStateWithholding)
    {
        var taxInfo = new TaxInformation
        {
            EmployeeId = employeeId,
            FederalFilingStatus = federalFilingStatus,
            FederalAllowances = federalAllowances,
            AdditionalFederalWithholding = additionalFederalWithholding,
            State = state,
            StateFilingStatus = stateFilingStatus,
            StateAllowances = stateAllowances,
            AdditionalStateWithholding = additionalStateWithholding
        };

        taxInfo.AddDomainEvent(new TaxInformationCreatedEvent(taxInfo.Id, employeeId));
        return taxInfo;
    }

    public void Update(
        string federalFilingStatus,
        int federalAllowances,
        decimal additionalFederalWithholding,
        string state,
        string stateFilingStatus,
        int stateAllowances,
        decimal additionalStateWithholding)
    {
        FederalFilingStatus = federalFilingStatus;
        FederalAllowances = federalAllowances;
        AdditionalFederalWithholding = additionalFederalWithholding;
        State = state;
        StateFilingStatus = stateFilingStatus;
        StateAllowances = stateAllowances;
        AdditionalStateWithholding = additionalStateWithholding;
        SetUpdated();

        AddDomainEvent(new TaxInformationUpdatedEvent(Id, EmployeeId));
    }
}
