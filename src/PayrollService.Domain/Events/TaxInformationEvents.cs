using PayrollService.Domain.Common;

namespace PayrollService.Domain.Events;

public class TaxInformationCreatedEvent : DomainEvent
{
    public override string EventType => "taxinfo.created";
    public Guid TaxInfoId { get; }
    public Guid EmployeeId { get; }

    public TaxInformationCreatedEvent(Guid taxInfoId, Guid employeeId)
    {
        TaxInfoId = taxInfoId;
        EmployeeId = employeeId;
    }
}

public class TaxInformationUpdatedEvent : DomainEvent
{
    public override string EventType => "taxinfo.updated";
    public Guid TaxInfoId { get; }
    public Guid EmployeeId { get; }

    public TaxInformationUpdatedEvent(Guid taxInfoId, Guid employeeId)
    {
        TaxInfoId = taxInfoId;
        EmployeeId = employeeId;
    }
}
