namespace PayrollService.Infrastructure.Orleans.State;

[GenerateSerializer]
public class TimeEntryState
{
    [Id(0)]
    public Guid Id { get; set; }

    [Id(1)]
    public Guid EmployeeId { get; set; }

    [Id(2)]
    public DateTime ClockIn { get; set; }

    [Id(3)]
    public DateTime? ClockOut { get; set; }

    [Id(4)]
    public decimal HoursWorked { get; set; }

    [Id(5)]
    public DateTime CreatedAt { get; set; }

    [Id(6)]
    public DateTime UpdatedAt { get; set; }

    [Id(7)]
    public bool IsInitialized { get; set; }
}
