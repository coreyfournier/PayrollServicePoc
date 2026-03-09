using MediatR;
using PayrollService.Application.DTOs;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Queries.BankAccount;

public record GetBankAccountsByEmployeeQuery(Guid EmployeeId) : IRequest<IEnumerable<BankAccountDto>>;

public class GetBankAccountsByEmployeeQueryHandler : IRequestHandler<GetBankAccountsByEmployeeQuery, IEnumerable<BankAccountDto>>
{
    private readonly IBankAccountRepository _repository;

    public GetBankAccountsByEmployeeQueryHandler(IBankAccountRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<BankAccountDto>> Handle(GetBankAccountsByEmployeeQuery request, CancellationToken cancellationToken)
    {
        var accounts = await _repository.GetByEmployeeIdAsync(request.EmployeeId, cancellationToken);

        return accounts.Select(a => new BankAccountDto(
            a.Id, a.EmployeeId, a.BankName,
            a.AccountNumberMasked, a.RoutingNumber,
            a.AccountType, a.IsActive,
            a.CreatedAt, a.UpdatedAt));
    }
}
