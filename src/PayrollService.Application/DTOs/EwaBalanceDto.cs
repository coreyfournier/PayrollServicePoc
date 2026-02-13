namespace PayrollService.Application.DTOs;

public record EwaBalanceDto(
    Guid EmployeeId,
    DateTime CalculationTimestamp,
    decimal GrossBalance,
    decimal NetBalance,
    decimal FinalBalance,
    decimal AccessPercentage,
    decimal MinimumThreshold,
    bool IsTransferEligible,
    string Currency,
    decimal DailyTransferLimit,
    decimal RemainingDailyLimit,
    decimal OutstandingAdvances,
    string CalculationMethod,
    decimal HoursWorked,
    decimal PayRate,
    List<EwaDeductionItemDto>? Deductions,
    EwaBalanceUnavailableReasonDto? BalanceUnavailableReason);

public record EwaDeductionItemDto(
    string Type,
    string Name,
    decimal Amount);

public record EwaBalanceUnavailableReasonDto(
    string Code,
    string Message);
