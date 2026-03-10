using MediatR;
using TransferService.Application.DTOs;

namespace TransferService.Application.Commands.Transfer;

public record InitiateTransferCommand(
    Guid EmployeeId, decimal Amount, long PayPeriodNumber, Guid BankAccountId) : IRequest<TransferDto>;
