using PayrollService.Domain.Entities;

namespace PayrollService.Domain.Repositories;

public interface ITimeEntryRepository
{
    Task<TimeEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<TimeEntry>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<TimeEntry?> GetActiveEntryByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<TimeEntry> AddAsync(TimeEntry timeEntry, CancellationToken cancellationToken = default);
    Task UpdateAsync(TimeEntry timeEntry, CancellationToken cancellationToken = default);
}
