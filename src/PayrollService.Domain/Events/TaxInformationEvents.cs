using PayrollService.Domain.Common;

namespace PayrollService.Domain.Events;

public class TaxInformationCreatedEvent : DomainEvent
{
    public override string EventType => "taxinfo.created";
    public Guid TaxInfoId { get; }
    public Guid EmployeeId { get; }
    public string FederalFilingStatus { get; }
    public int FederalAllowances { get; }
    public decimal AdditionalFederalWithholding { get; }
    public string State { get; }
    public string StateFilingStatus { get; }
    public int StateAllowances { get; }
    public decimal AdditionalStateWithholding { get; }

    public TaxInformationCreatedEvent(
        Guid taxInfoId,
        Guid employeeId,
        string federalFilingStatus,
        int federalAllowances,
        decimal additionalFederalWithholding,
        string state,
        string stateFilingStatus,
        int stateAllowances,
        decimal additionalStateWithholding)
    {
        TaxInfoId = taxInfoId;
        EmployeeId = employeeId;
        FederalFilingStatus = federalFilingStatus;
        FederalAllowances = federalAllowances;
        AdditionalFederalWithholding = additionalFederalWithholding;
        State = state;
        StateFilingStatus = stateFilingStatus;
        StateAllowances = stateAllowances;
        AdditionalStateWithholding = additionalStateWithholding;
    }
}

public class TaxInformationUpdatedEvent : DomainEvent
{
    public override string EventType => "taxinfo.updated";
    public Guid TaxInfoId { get; }
    public Guid EmployeeId { get; }
    public string FederalFilingStatus { get; }
    public int FederalAllowances { get; }
    public decimal AdditionalFederalWithholding { get; }
    public string State { get; }
    public string StateFilingStatus { get; }
    public int StateAllowances { get; }
    public decimal AdditionalStateWithholding { get; }

    public TaxInformationUpdatedEvent(
        Guid taxInfoId,
        Guid employeeId,
        string federalFilingStatus,
        int federalAllowances,
        decimal additionalFederalWithholding,
        string state,
        string stateFilingStatus,
        int stateAllowances,
        decimal additionalStateWithholding)
    {
        TaxInfoId = taxInfoId;
        EmployeeId = employeeId;
        FederalFilingStatus = federalFilingStatus;
        FederalAllowances = federalAllowances;
        AdditionalFederalWithholding = additionalFederalWithholding;
        State = state;
        StateFilingStatus = stateFilingStatus;
        StateAllowances = stateAllowances;
        AdditionalStateWithholding = additionalStateWithholding;
    }
}
