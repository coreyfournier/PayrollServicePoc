using MongoDB.Driver;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Persistence;

namespace PayrollService.Infrastructure.Repositories;

public class TimeEntryRepository : ITimeEntryRepository
{
    private readonly MongoDbContext _context;

    public TimeEntryRepository(MongoDbContext context)
    {
        _context = context;
    }

    public async Task<TimeEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.TimeEntries
            .Find(t => t.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<TimeEntry>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        return await _context.TimeEntries
            .Find(t => t.EmployeeId == employeeId)
            .SortByDescending(t => t.ClockIn)
            .ToListAsync(cancellationToken);
    }

    public async Task<TimeEntry?> GetActiveEntryByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        return await _context.TimeEntries
            .Find(t => t.EmployeeId == employeeId && t.ClockOut == null)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<TimeEntry> AddAsync(TimeEntry timeEntry, CancellationToken cancellationToken = default)
    {
        await _context.TimeEntries.InsertOneAsync(timeEntry, cancellationToken: cancellationToken);
        return timeEntry;
    }

    public async Task UpdateAsync(TimeEntry timeEntry, CancellationToken cancellationToken = default)
    {
        await _context.TimeEntries.ReplaceOneAsync(
            t => t.Id == timeEntry.Id,
            timeEntry,
            cancellationToken: cancellationToken);
    }
}
