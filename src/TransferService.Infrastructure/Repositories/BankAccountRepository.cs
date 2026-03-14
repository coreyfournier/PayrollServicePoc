using MongoDB.Driver;
using TransferService.Domain.Entities;
using TransferService.Domain.Repositories;
using TransferService.Infrastructure.Persistence;

namespace TransferService.Infrastructure.Repositories;

public class BankAccountRepository : IBankAccountRepository
{
    private readonly TransferMongoDbContext _dbContext;

    public BankAccountRepository(TransferMongoDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BankAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.BankAccounts.Find(b => b.Id == id).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<BankAccount>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.BankAccounts
            .Find(b => b.EmployeeId == employeeId)
            .ToListAsync(cancellationToken);
    }

    public async Task<BankAccount> AddAsync(BankAccount bankAccount, CancellationToken cancellationToken = default)
    {
        await _dbContext.BankAccounts.ReplaceOneAsync(
            b => b.Id == bankAccount.Id,
            bankAccount,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
        return bankAccount;
    }

    public async Task UpdateAsync(BankAccount bankAccount, CancellationToken cancellationToken = default)
    {
        await _dbContext.BankAccounts.ReplaceOneAsync(
            b => b.Id == bankAccount.Id,
            bankAccount,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }
}
