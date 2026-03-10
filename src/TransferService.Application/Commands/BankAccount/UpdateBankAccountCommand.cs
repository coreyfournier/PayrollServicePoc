using MediatR;
using TransferService.Application.DTOs;
using TransferService.Application.Interfaces;
using TransferService.Domain.Repositories;

namespace TransferService.Application.Commands.BankAccount;

public record UpdateBankAccountCommand(
    Guid Id,
    string BankName,
    string AccountNumberMasked,
    string RoutingNumber,
    Domain.Enums.BankAccountType AccountType) : IRequest<BankAccountDto>;

public class UpdateBankAccountCommandHandler : IRequestHandler<UpdateBankAccountCommand, BankAccountDto>
{
    private readonly IBankAccountRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateBankAccountCommandHandler(IBankAccountRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<BankAccountDto> Handle(UpdateBankAccountCommand request, CancellationToken cancellationToken)
    {
        var bankAccount = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Bank account {request.Id} not found.");

        bankAccount.Update(request.BankName, request.AccountNumberMasked, request.RoutingNumber, request.AccountType);

        await _unitOfWork.ExecuteAsync(
            async () => await _repository.UpdateAsync(bankAccount, cancellationToken),
            bankAccount,
            cancellationToken);

        return new BankAccountDto(
            bankAccount.Id, bankAccount.EmployeeId, bankAccount.BankName,
            bankAccount.AccountNumberMasked, bankAccount.RoutingNumber,
            bankAccount.AccountType, bankAccount.IsActive,
            bankAccount.CreatedAt, bankAccount.UpdatedAt);
    }
}
