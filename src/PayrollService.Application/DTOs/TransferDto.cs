using PayrollService.Domain.Enums;

namespace PayrollService.Application.DTOs;

public record TransferDto(
    Guid Id,
    Guid EmployeeId,
    decimal Amount,
    long PayPeriodNumber,
    TransferStatus Status,
    Guid BankAccountId,
    DateTime InitiatedAt,
    DateTime? CompletedAt,
    string? FailureReason,
    string? ExternalReferenceId,
    decimal? CurrentBalance,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record InitiateTransferDto(
    Guid EmployeeId,
    decimal Amount,
    long PayPeriodNumber,
    Guid BankAccountId);
