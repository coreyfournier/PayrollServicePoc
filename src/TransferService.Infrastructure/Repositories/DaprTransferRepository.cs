using System.Text.Json;
using Dapr.Client;
using MongoDB.Driver;
using TransferService.Domain.Entities;
using TransferService.Domain.Repositories;
using TransferService.Infrastructure.Persistence;
using TransferService.Infrastructure.StateStore;

namespace TransferService.Infrastructure.Repositories;

public class DaprTransferRepository : ITransferRepository
{
    private const string StateStoreName = "statestore-transfers";
    private readonly DaprClient _daprClient;
    private readonly TransferMongoDbContext _dbContext;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public DaprTransferRepository(DaprClient daprClient, TransferMongoDbContext dbContext)
    {
        _daprClient = daprClient;
        _dbContext = dbContext;
    }

    public async Task<Transfer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = StateKeyHelper.GetTransferKey(id);
            var json = await _daprClient.GetStateAsync<string>(StateStoreName, key, cancellationToken: cancellationToken);
            if (!string.IsNullOrEmpty(json))
                return JsonSerializer.Deserialize<Transfer>(json, JsonOptions);
        }
        catch { }

        return await _dbContext.Transfers.Find(t => t.Id == id).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<Transfer>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Transfers
            .Find(t => t.EmployeeId == employeeId)
            .SortByDescending(t => t.InitiatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Transfer>> GetByEmployeeAndPayPeriodAsync(Guid employeeId, long payPeriodNumber, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Transfers
            .Find(t => t.EmployeeId == employeeId && t.PayPeriodNumber == payPeriodNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountByEmployeeAndDateAsync(Guid employeeId, DateTime date, CancellationToken cancellationToken = default)
    {
        var nextDay = date.AddDays(1);
        return (int)await _dbContext.Transfers
            .CountDocumentsAsync(t => t.EmployeeId == employeeId && t.InitiatedAt >= date && t.InitiatedAt < nextDay, cancellationToken: cancellationToken);
    }

    public async Task<IEnumerable<Transfer>> GetRecentAsync(int limit = 50, string? statusFilter = null, CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<Transfer>.Filter;
        var filter = filterBuilder.Empty;

        if (!string.IsNullOrEmpty(statusFilter))
        {
            var status = Enum.Parse<Domain.Enums.TransferStatus>(statusFilter, ignoreCase: true);
            filter = filterBuilder.Eq(t => t.Status, status);
        }

        return await _dbContext.Transfers
            .Find(filter)
            .SortByDescending(t => t.InitiatedAt)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<Transfer> AddAsync(Transfer transfer, CancellationToken cancellationToken = default)
    {
        await _dbContext.Transfers.ReplaceOneAsync(
            t => t.Id == transfer.Id,
            transfer,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
        return transfer;
    }

    public async Task UpdateAsync(Transfer transfer, CancellationToken cancellationToken = default)
    {
        await _dbContext.Transfers.ReplaceOneAsync(
            t => t.Id == transfer.Id,
            transfer,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }
}
