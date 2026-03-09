using MediatR;
using PayrollService.Application.DTOs;

namespace PayrollService.Application.Commands.Transfer;

public record InitiateTransferCommand(
    Guid EmployeeId,
    decimal Amount,
    long PayPeriodNumber,
    Guid BankAccountId) : IRequest<TransferDto>;
