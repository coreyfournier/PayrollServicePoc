using ListenerApi.Data.Entities;

namespace ListenerApi.Data.Repositories;

public interface IBankAccountRepository
{
    Task<BankAccount?> GetByIdAsync(Guid id);
    Task<List<BankAccount>> GetByEmployeeIdAsync(Guid employeeId);
    Task<BankAccount> AddAsync(BankAccount bankAccount);
    Task UpdateAsync(BankAccount bankAccount);
}
