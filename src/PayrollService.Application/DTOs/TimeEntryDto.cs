namespace PayrollService.Application.DTOs;

public record TimeEntryDto(
    Guid Id,
    Guid EmployeeId,
    DateTime ClockIn,
    DateTime? ClockOut,
    decimal HoursWorked,
    DateTime CreatedAt,
    DateTime UpdatedAt);
