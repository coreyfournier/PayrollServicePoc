using System.Text.Json;
using Dapr.Client;
using MongoDB.Driver;
using TransferService.Domain.Entities;
using TransferService.Domain.Repositories;
using TransferService.Infrastructure.Persistence;
using TransferService.Infrastructure.StateStore;

namespace TransferService.Infrastructure.Repositories;

public class DaprBankAccountRepository : IBankAccountRepository
{
    private const string StateStoreName = "statestore-transfers";
    private readonly DaprClient _daprClient;
    private readonly TransferMongoDbContext _dbContext;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public DaprBankAccountRepository(DaprClient daprClient, TransferMongoDbContext dbContext)
    {
        _daprClient = daprClient;
        _dbContext = dbContext;
    }

    public async Task<BankAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = StateKeyHelper.GetBankAccountKey(id);
            var json = await _daprClient.GetStateAsync<string>(StateStoreName, key, cancellationToken: cancellationToken);
            if (!string.IsNullOrEmpty(json))
                return JsonSerializer.Deserialize<BankAccount>(json, JsonOptions);
        }
        catch { }

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
