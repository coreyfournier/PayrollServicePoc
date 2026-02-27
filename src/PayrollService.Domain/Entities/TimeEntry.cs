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

    public static TimeEntry Create(Guid employeeId, DateTime clockIn, DateTime? clockOut)
    {
        if (clockOut.HasValue && clockOut.Value <= clockIn)
            throw new InvalidOperationException("Clock out must be after clock in.");

        var hoursWorked = clockOut.HasValue
            ? Math.Round((decimal)(clockOut.Value - clockIn).TotalHours, 2)
            : 0m;

        var timeEntry = new TimeEntry
        {
            EmployeeId = employeeId,
            ClockIn = clockIn,
            ClockOut = clockOut,
            HoursWorked = hoursWorked
        };

        if (clockOut.HasValue)
            timeEntry.AddDomainEvent(new EmployeeClockedOutEvent(timeEntry.Id, employeeId, clockIn, clockOut.Value, hoursWorked));
        else
            timeEntry.AddDomainEvent(new EmployeeClockedInEvent(timeEntry.Id, employeeId, clockIn));

        return timeEntry;
    }

    public void ClockOutEmployee()
    {
        if (ClockOut.HasValue)
            throw new InvalidOperationException("Employee has already clocked out for this entry.");

        ClockOut = DateTime.UtcNow;
        HoursWorked = Math.Round((decimal)(ClockOut.Value - ClockIn).TotalHours, 2);
        SetUpdated();

        AddDomainEvent(new EmployeeClockedOutEvent(Id, EmployeeId, ClockIn, ClockOut.Value, HoursWorked));
    }

    public void UpdateTimes(DateTime clockIn, DateTime? clockOut)
    {
        if (clockOut.HasValue && clockOut.Value <= clockIn)
            throw new InvalidOperationException("Clock out must be after clock in.");

        ClockIn = clockIn;
        ClockOut = clockOut;
        HoursWorked = clockOut.HasValue
            ? Math.Round((decimal)(clockOut.Value - clockIn).TotalHours, 2)
            : 0;
        SetUpdated();
        AddDomainEvent(new TimeEntryUpdatedEvent(Id, EmployeeId, ClockIn, ClockOut, HoursWorked));
    }
}
