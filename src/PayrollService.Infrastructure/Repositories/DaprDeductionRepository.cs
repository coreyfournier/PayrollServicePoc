using System.Text.Json;
using Dapr.Client;
using MongoDB.Driver;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Persistence;
using PayrollService.Infrastructure.StateStore;

namespace PayrollService.Infrastructure.Repositories;

public class DaprDeductionRepository : IDeductionRepository
{
    private readonly DaprClient _daprClient;
    private readonly MongoDbContext _mongoContext;
    private const string StateStoreName = "statestore-mongodb";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DaprDeductionRepository(DaprClient daprClient, MongoDbContext mongoContext)
    {
        _daprClient = daprClient;
        _mongoContext = mongoContext;
    }

    public async Task<Deduction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var stateKey = StateKeyHelper.GetDeductionKey(id);

        try
        {
            var deduction = await _daprClient.GetStateAsync<Deduction>(StateStoreName, stateKey, cancellationToken: cancellationToken);
            if (deduction != null)
            {
                return deduction;
            }
        }
        catch
        {
            // Fallback to MongoDB if Dapr state store fails
        }

        return await _mongoContext.Deductions
            .Find(d => d.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<Deduction>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        return await _mongoContext.Deductions
            .Find(d => d.EmployeeId == employeeId)
            .ToListAsync(cancellationToken);
    }

    public async Task<Deduction> AddAsync(Deduction deduction, CancellationToken cancellationToken = default)
    {
        await _mongoContext.Deductions.InsertOneAsync(deduction, cancellationToken: cancellationToken);
        return deduction;
    }

    public async Task UpdateAsync(Deduction deduction, CancellationToken cancellationToken = default)
    {
        await _mongoContext.Deductions.ReplaceOneAsync(
            d => d.Id == deduction.Id,
            deduction,
            cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var stateKey = StateKeyHelper.GetDeductionKey(id);

        try
        {
            await _daprClient.DeleteStateAsync(StateStoreName, stateKey, cancellationToken: cancellationToken);
        }
        catch
        {
            // Continue with MongoDB deletion even if Dapr fails
        }

        await _mongoContext.Deductions.DeleteOneAsync(d => d.Id == id, cancellationToken);
    }
}
