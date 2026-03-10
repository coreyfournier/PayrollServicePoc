using TransferService.Domain.Entities;

namespace TransferService.Domain.Repositories;

public interface IBankAccountRepository
{
    Task<BankAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<BankAccount>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<BankAccount> AddAsync(BankAccount bankAccount, CancellationToken cancellationToken = default);
    Task UpdateAsync(BankAccount bankAccount, CancellationToken cancellationToken = default);
}
