using TransferService.Domain.Enums;

namespace TransferService.Application.DTOs;

public record BankAccountDto(
    Guid Id,
    Guid EmployeeId,
    string BankName,
    string AccountNumberMasked,
    string RoutingNumber,
    BankAccountType AccountType,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateBankAccountDto(
    Guid EmployeeId,
    string BankName,
    string AccountNumberMasked,
    string RoutingNumber,
    BankAccountType AccountType);

public record UpdateBankAccountDto(
    string BankName,
    string AccountNumberMasked,
    string RoutingNumber,
    BankAccountType AccountType);
