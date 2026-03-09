using MediatR;
using PayrollService.Application.DTOs;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Commands.BankAccount;

public record CreateBankAccountCommand(
    Guid EmployeeId,
    string BankName,
    string AccountNumberMasked,
    string RoutingNumber,
    BankAccountType AccountType) : IRequest<BankAccountDto>;

public class CreateBankAccountCommandHandler : IRequestHandler<CreateBankAccountCommand, BankAccountDto>
{
    private readonly IBankAccountRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateBankAccountCommandHandler(IBankAccountRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<BankAccountDto> Handle(CreateBankAccountCommand request, CancellationToken cancellationToken)
    {
        var bankAccount = Domain.Entities.BankAccount.Create(
            request.EmployeeId,
            request.BankName,
            request.AccountNumberMasked,
            request.RoutingNumber,
            request.AccountType);

        var result = await _unitOfWork.ExecuteAsync(
            async () => await _repository.AddAsync(bankAccount, cancellationToken),
            bankAccount,
            cancellationToken);

        return new BankAccountDto(
            result.Id, result.EmployeeId, result.BankName,
            result.AccountNumberMasked, result.RoutingNumber,
            result.AccountType, result.IsActive,
            result.CreatedAt, result.UpdatedAt);
    }
}
