using System.Text.Json;
using Dapr.Client;
using MongoDB.Driver;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Persistence;
using PayrollService.Infrastructure.StateStore;

namespace PayrollService.Infrastructure.Repositories;

public class DaprTaxInformationRepository : ITaxInformationRepository
{
    private readonly DaprClient _daprClient;
    private readonly MongoDbContext _mongoContext;
    private const string StateStoreName = "statestore-mongodb";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DaprTaxInformationRepository(DaprClient daprClient, MongoDbContext mongoContext)
    {
        _daprClient = daprClient;
        _mongoContext = mongoContext;
    }

    public async Task<TaxInformation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var stateKey = StateKeyHelper.GetTaxInformationKey(id);

        try
        {
            var taxInfo = await _daprClient.GetStateAsync<TaxInformation>(StateStoreName, stateKey, cancellationToken: cancellationToken);
            if (taxInfo != null)
            {
                return taxInfo;
            }
        }
        catch
        {
            // Fallback to MongoDB if Dapr state store fails
        }

        return await _mongoContext.TaxInformation
            .Find(t => t.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<TaxInformation?> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        return await _mongoContext.TaxInformation
            .Find(t => t.EmployeeId == employeeId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<TaxInformation> AddAsync(TaxInformation taxInformation, CancellationToken cancellationToken = default)
    {
        await _mongoContext.TaxInformation.InsertOneAsync(taxInformation, cancellationToken: cancellationToken);
        return taxInformation;
    }

    public async Task UpdateAsync(TaxInformation taxInformation, CancellationToken cancellationToken = default)
    {
        await _mongoContext.TaxInformation.ReplaceOneAsync(
            t => t.Id == taxInformation.Id,
            taxInformation,
            cancellationToken: cancellationToken);
    }
}
