using Dapr.Client;
using MongoDB.Driver;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Persistence;
using PayrollService.Infrastructure.StateStore;

namespace PayrollService.Infrastructure.Repositories;

public class DaprBankAccountRepository : IBankAccountRepository
{
    private readonly DaprClient _daprClient;
    private readonly MongoDbContext _mongoContext;
    private const string StateStoreName = "statestore-mongodb";

    public DaprBankAccountRepository(DaprClient daprClient, MongoDbContext mongoContext)
    {
        _daprClient = daprClient;
        _mongoContext = mongoContext;
    }

    public async Task<BankAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var stateKey = StateKeyHelper.GetBankAccountKey(id);

        try
        {
            var account = await _daprClient.GetStateAsync<BankAccount>(StateStoreName, stateKey, cancellationToken: cancellationToken);
            if (account != null)
                return account;
        }
        catch
        {
            // Fallback to MongoDB
        }

        return await _mongoContext.BankAccounts
            .Find(a => a.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<BankAccount>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        return await _mongoContext.BankAccounts
            .Find(a => a.EmployeeId == employeeId)
            .ToListAsync(cancellationToken);
    }

    public async Task<BankAccount> AddAsync(BankAccount bankAccount, CancellationToken cancellationToken = default)
    {
        await _mongoContext.BankAccounts.ReplaceOneAsync(
            a => a.Id == bankAccount.Id,
            bankAccount,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken: cancellationToken);
        return bankAccount;
    }

    public async Task UpdateAsync(BankAccount bankAccount, CancellationToken cancellationToken = default)
    {
        await _mongoContext.BankAccounts.ReplaceOneAsync(
            a => a.Id == bankAccount.Id,
            bankAccount,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken: cancellationToken);
    }
}
