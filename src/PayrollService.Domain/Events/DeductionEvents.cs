using PayrollService.Domain.Common;
using PayrollService.Domain.Enums;

namespace PayrollService.Domain.Events;

public class DeductionCreatedEvent : DomainEvent
{
    public override string EventType => "deduction.created";
    public Guid DeductionId { get; }
    public Guid EmployeeId { get; }
    public DeductionType DeductionType { get; }
    public decimal Amount { get; }

    public DeductionCreatedEvent(Guid deductionId, Guid employeeId, DeductionType deductionType, decimal amount)
    {
        DeductionId = deductionId;
        EmployeeId = employeeId;
        DeductionType = deductionType;
        Amount = amount;
    }
}

public class DeductionUpdatedEvent : DomainEvent
{
    public override string EventType => "deduction.updated";
    public Guid DeductionId { get; }
    public Guid EmployeeId { get; }
    public DeductionType DeductionType { get; }
    public decimal Amount { get; }

    public DeductionUpdatedEvent(Guid deductionId, Guid employeeId, DeductionType deductionType, decimal amount)
    {
        DeductionId = deductionId;
        EmployeeId = employeeId;
        DeductionType = deductionType;
        Amount = amount;
    }
}

public class DeductionDeactivatedEvent : DomainEvent
{
    public override string EventType => "deduction.deactivated";
    public Guid DeductionId { get; }
    public Guid EmployeeId { get; }

    public DeductionDeactivatedEvent(Guid deductionId, Guid employeeId)
    {
        DeductionId = deductionId;
        EmployeeId = employeeId;
    }
}
