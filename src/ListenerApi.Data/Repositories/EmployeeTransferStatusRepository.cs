using ListenerApi.Data.DbContext;
using ListenerApi.Data.Entities;

namespace ListenerApi.Data.Repositories;

public class EmployeeTransferStatusRepository : IEmployeeTransferStatusRepository
{
    private readonly ListenerDbContext _context;

    public EmployeeTransferStatusRepository(ListenerDbContext context)
    {
        _context = context;
    }

    public async Task<EmployeeTransferStatus?> GetByEmployeeIdAsync(Guid employeeId)
        => await _context.EmployeeTransferStatuses.FindAsync(employeeId);

    public async Task UpsertAsync(EmployeeTransferStatus status)
    {
        var existing = await _context.EmployeeTransferStatuses.FindAsync(status.EmployeeId);
        if (existing == null)
        {
            _context.EmployeeTransferStatuses.Add(status);
        }
        else
        {
            _context.Entry(existing).CurrentValues.SetValues(status);
        }
        await _context.SaveChangesAsync();
    }
}
