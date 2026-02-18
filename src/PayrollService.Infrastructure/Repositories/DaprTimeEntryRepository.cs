using System.Text.Json;
using Dapr.Client;
using MongoDB.Driver;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Persistence;
using PayrollService.Infrastructure.StateStore;

namespace PayrollService.Infrastructure.Repositories;

public class DaprTimeEntryRepository : ITimeEntryRepository
{
    private readonly DaprClient _daprClient;
    private readonly MongoDbContext _mongoContext;
    private const string StateStoreName = "statestore-mongodb";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DaprTimeEntryRepository(DaprClient daprClient, MongoDbContext mongoContext)
    {
        _daprClient = daprClient;
        _mongoContext = mongoContext;
    }

    public async Task<TimeEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var stateKey = StateKeyHelper.GetTimeEntryKey(id);

        try
        {
            var timeEntry = await _daprClient.GetStateAsync<TimeEntry>(StateStoreName, stateKey, cancellationToken: cancellationToken);
            if (timeEntry != null)
            {
                return timeEntry;
            }
        }
        catch
        {
            // Fallback to MongoDB if Dapr state store fails
        }

        return await _mongoContext.TimeEntries
            .Find(t => t.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<TimeEntry>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        return await _mongoContext.TimeEntries
            .Find(t => t.EmployeeId == employeeId)
            .SortByDescending(t => t.ClockIn)
            .ToListAsync(cancellationToken);
    }

    public async Task<TimeEntry?> GetActiveEntryByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        return await _mongoContext.TimeEntries
            .Find(t => t.EmployeeId == employeeId && t.ClockOut == null)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<TimeEntry> AddAsync(TimeEntry timeEntry, CancellationToken cancellationToken = default)
    {
        await _mongoContext.TimeEntries.ReplaceOneAsync(
            t => t.Id == timeEntry.Id,
            timeEntry,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken: cancellationToken);
        return timeEntry;
    }

    public async Task UpdateAsync(TimeEntry timeEntry, CancellationToken cancellationToken = default)
    {
        await _mongoContext.TimeEntries.ReplaceOneAsync(
            t => t.Id == timeEntry.Id,
            timeEntry,
            cancellationToken: cancellationToken);
    }
}
