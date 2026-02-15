using System.Text.Json;
using Dapr.Client;
using MongoDB.Driver;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Persistence;
using PayrollService.Infrastructure.StateStore;

namespace PayrollService.Infrastructure.Repositories;

public class DaprEmployeeRepository : IEmployeeRepository
{
    private readonly DaprClient _daprClient;
    private readonly MongoDbContext _mongoContext;
    private const string StateStoreName = "statestore-mongodb";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DaprEmployeeRepository(DaprClient daprClient, MongoDbContext mongoContext)
    {
        _daprClient = daprClient;
        _mongoContext = mongoContext;
    }

    public async Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var stateKey = StateKeyHelper.GetEmployeeKey(id);

        try
        {
            var employee = await _daprClient.GetStateAsync<Employee>(StateStoreName, stateKey, cancellationToken: cancellationToken);
            if (employee != null)
            {
                return employee;
            }
        }
        catch
        {
            // Fallback to MongoDB if Dapr state store fails
        }

        return await _mongoContext.Employees
            .Find(e => e.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<Employee>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _mongoContext.Employees
            .Find(_ => true)
            .ToListAsync(cancellationToken);
    }

    public async Task<Employee> AddAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        await _mongoContext.Employees.ReplaceOneAsync(
            e => e.Id == employee.Id,
            employee,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken: cancellationToken);
        return employee;
    }

    public async Task UpdateAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        await _mongoContext.Employees.ReplaceOneAsync(
            e => e.Id == employee.Id,
            employee,
            cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var stateKey = StateKeyHelper.GetEmployeeKey(id);

        try
        {
            await _daprClient.DeleteStateAsync(StateStoreName, stateKey, cancellationToken: cancellationToken);
        }
        catch
        {
            // Continue with MongoDB deletion even if Dapr fails
        }

        await _mongoContext.Employees.DeleteOneAsync(e => e.Id == id, cancellationToken);
    }
}
