using PayrollService.Domain.Enums;

namespace PayrollService.Application.DTOs;

public record DeductionDto(
    Guid Id,
    Guid EmployeeId,
    DeductionType DeductionType,
    string Description,
    decimal Amount,
    bool IsPercentage,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateDeductionDto(
    Guid EmployeeId,
    DeductionType DeductionType,
    string Description,
    decimal Amount,
    bool IsPercentage);

public record UpdateDeductionDto(
    DeductionType DeductionType,
    string Description,
    decimal Amount,
    bool IsPercentage);
