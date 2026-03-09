using System.Text.Json;
using Dapr.Client;
using MongoDB.Driver;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Persistence;
using PayrollService.Infrastructure.StateStore;

namespace PayrollService.Infrastructure.Repositories;

public class DaprTransferRepository : ITransferRepository
{
    private readonly DaprClient _daprClient;
    private readonly MongoDbContext _mongoContext;
    private const string StateStoreName = "statestore-transfers";

    public DaprTransferRepository(DaprClient daprClient, MongoDbContext mongoContext)
    {
        _daprClient = daprClient;
        _mongoContext = mongoContext;
    }

    public async Task<Transfer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var stateKey = StateKeyHelper.GetTransferKey(id);

        try
        {
            var transfer = await _daprClient.GetStateAsync<Transfer>(StateStoreName, stateKey, cancellationToken: cancellationToken);
            if (transfer != null)
                return transfer;
        }
        catch
        {
            // Fallback to MongoDB
        }

        return await _mongoContext.Transfers
            .Find(t => t.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<Transfer>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        return await _mongoContext.Transfers
            .Find(t => t.EmployeeId == employeeId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Transfer>> GetByEmployeeAndPayPeriodAsync(Guid employeeId, long payPeriodNumber, CancellationToken cancellationToken = default)
    {
        return await _mongoContext.Transfers
            .Find(t => t.EmployeeId == employeeId && t.PayPeriodNumber == payPeriodNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountByEmployeeAndDateAsync(Guid employeeId, DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Transfer>.Filter.And(
            Builders<Transfer>.Filter.Eq(t => t.EmployeeId, employeeId),
            Builders<Transfer>.Filter.Gte(t => t.InitiatedAt, sinceUtc),
            Builders<Transfer>.Filter.Ne(t => t.Status, TransferStatus.Failed));

        return (int)await _mongoContext.Transfers.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }

    public async Task<Transfer> AddAsync(Transfer transfer, CancellationToken cancellationToken = default)
    {
        await _mongoContext.Transfers.ReplaceOneAsync(
            t => t.Id == transfer.Id,
            transfer,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken: cancellationToken);
        return transfer;
    }

    public async Task UpdateAsync(Transfer transfer, CancellationToken cancellationToken = default)
    {
        await _mongoContext.Transfers.ReplaceOneAsync(
            t => t.Id == transfer.Id,
            transfer,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken: cancellationToken);
    }
}
