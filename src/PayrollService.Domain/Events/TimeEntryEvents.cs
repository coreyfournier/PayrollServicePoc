using PayrollService.Domain.Common;

namespace PayrollService.Domain.Events;

public class EmployeeClockedInEvent : DomainEvent
{
    public override string EventType => "timeentry.clockedin";
    public Guid TimeEntryId { get; }
    public Guid EmployeeId { get; }
    public DateTime ClockInTime { get; }

    public EmployeeClockedInEvent(Guid timeEntryId, Guid employeeId, DateTime clockInTime)
    {
        TimeEntryId = timeEntryId;
        EmployeeId = employeeId;
        ClockInTime = clockInTime;
    }
}

public class EmployeeClockedOutEvent : DomainEvent
{
    public override string EventType => "timeentry.clockedout";
    public Guid TimeEntryId { get; }
    public Guid EmployeeId { get; }
    public DateTime ClockInTime { get; }
    public DateTime ClockOutTime { get; }
    public decimal HoursWorked { get; }

    public EmployeeClockedOutEvent(Guid timeEntryId, Guid employeeId, DateTime clockInTime, DateTime clockOutTime, decimal hoursWorked)
    {
        TimeEntryId = timeEntryId;
        EmployeeId = employeeId;
        ClockInTime = clockInTime;
        ClockOutTime = clockOutTime;
        HoursWorked = hoursWorked;
    }
}
