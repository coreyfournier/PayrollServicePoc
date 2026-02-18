using PayrollService.Domain.Common;
using PayrollService.Domain.Enums;

namespace PayrollService.Domain.Events;

public class EmployeeCreatedEvent : DomainEvent
{
    public override string EventType => "employee.created";
    public Guid EmployeeId { get; }
    public string FirstName { get; }
    public string LastName { get; }
    public string Email { get; }

    public EmployeeCreatedEvent(Guid employeeId, string firstName, string lastName, string email)
    {
        EmployeeId = employeeId;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
    }
}

public class EmployeeUpdatedEvent : DomainEvent
{
    public override string EventType => "employee.updated";
    public Guid EmployeeId { get; }
    public string FirstName { get; }
    public string LastName { get; }
    public string Email { get; }
    public PayType PayType { get; }
    public decimal PayRate { get; }
    public decimal PayPeriodHours { get; }

    public EmployeeUpdatedEvent(Guid employeeId, string firstName, string lastName, string email, PayType payType, decimal payRate, decimal payPeriodHours = 40)
    {
        EmployeeId = employeeId;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        PayType = payType;
        PayRate = payRate;
        PayPeriodHours = payPeriodHours;
    }
}

public class EmployeeDeactivatedEvent : DomainEvent
{
    public override string EventType => "employee.deactivated";
    public Guid EmployeeId { get; }

    public EmployeeDeactivatedEvent(Guid employeeId)
    {
        EmployeeId = employeeId;
    }
}

public class EmployeeActivatedEvent : DomainEvent
{
    public override string EventType => "employee.activated";
    public Guid EmployeeId { get; }

    public EmployeeActivatedEvent(Guid employeeId)
    {
        EmployeeId = employeeId;
    }
}
