using ListenerApi.Data.DbContext;
using ListenerApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ListenerApi.Data.Repositories;

public class EmployeePayAttributesRepository : IEmployeePayAttributesRepository
{
    private readonly ListenerDbContext _context;

    public EmployeePayAttributesRepository(ListenerDbContext context)
    {
        _context = context;
    }

    public async Task<EmployeePayAttributes?> GetByEmployeeIdAsync(Guid employeeId)
        => await _context.EmployeePayAttributes.FindAsync(employeeId);

    public async Task UpsertAsync(EmployeePayAttributes payAttributes)
    {
        var existing = await _context.EmployeePayAttributes.FindAsync(payAttributes.EmployeeId);
        if (existing == null)
        {
            _context.EmployeePayAttributes.Add(payAttributes);
        }
        else
        {
            _context.Entry(existing).CurrentValues.SetValues(payAttributes);
        }
        await _context.SaveChangesAsync();
    }

    public async Task DeleteByEmployeeIdAsync(Guid employeeId)
    {
        var existing = await _context.EmployeePayAttributes.FindAsync(employeeId);
        if (existing != null)
        {
            _context.EmployeePayAttributes.Remove(existing);
            await _context.SaveChangesAsync();
        }
    }
}
