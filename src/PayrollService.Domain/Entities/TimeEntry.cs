using PayrollService.Domain.Common;
using PayrollService.Domain.Events;

namespace PayrollService.Domain.Entities;

public class TimeEntry : Entity
{
    public Guid EmployeeId { get; private set; }
    public DateTime ClockIn { get; private set; }
    public DateTime? ClockOut { get; private set; }
    public decimal HoursWorked { get; private set; }

    private TimeEntry() { }

    public static TimeEntry ClockInEmployee(Guid employeeId)
    {
        var timeEntry = new TimeEntry
        {
            EmployeeId = employeeId,
            ClockIn = DateTime.UtcNow,
            HoursWorked = 0
        };

        timeEntry.AddDomainEvent(new EmployeeClockedInEvent(timeEntry.Id, employeeId, timeEntry.ClockIn));
        return timeEntry;
    }

    public void ClockOutEmployee()
    {
        if (ClockOut.HasValue)
            throw new InvalidOperationException("Employee has already clocked out for this entry.");

        ClockOut = DateTime.UtcNow;
        HoursWorked = Math.Round((decimal)(ClockOut.Value - ClockIn).TotalHours, 2);
        SetUpdated();

        AddDomainEvent(new EmployeeClockedOutEvent(Id, EmployeeId, ClockOut.Value, HoursWorked));
    }
}
