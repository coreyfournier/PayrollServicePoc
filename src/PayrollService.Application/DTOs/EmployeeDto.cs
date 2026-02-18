using PayrollService.Domain.Enums;

namespace PayrollService.Application.DTOs;

public record EmployeeDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    PayType PayType,
    decimal PayRate,
    decimal PayPeriodHours,
    DateTime HireDate,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateEmployeeDto(
    string FirstName,
    string LastName,
    string Email,
    PayType PayType,
    decimal PayRate,
    DateTime HireDate,
    decimal PayPeriodHours = 40);

public record UpdateEmployeeDto(
    string FirstName,
    string LastName,
    string Email,
    PayType PayType,
    decimal PayRate,
    decimal PayPeriodHours = 40);
