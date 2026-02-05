using ListenerApi.Data.DbContext;
using ListenerApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ListenerApi.Data.Repositories;

public class EmployeeRecordRepository : IEmployeeRecordRepository
{
    private readonly ListenerDbContext _context;

    public EmployeeRecordRepository(ListenerDbContext context)
    {
        _context = context;
    }

    public async Task<EmployeeRecord?> GetByIdAsync(Guid id)
        => await _context.EmployeeRecords.FindAsync(id);

    public Task<IQueryable<EmployeeRecord>> GetAllAsync()
        => Task.FromResult(_context.EmployeeRecords.AsQueryable());

    public async Task AddAsync(EmployeeRecord record)
    {
        _context.EmployeeRecords.Add(record);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(EmployeeRecord record)
    {
        _context.EmployeeRecords.Update(record);
        await _context.SaveChangesAsync();
    }

    public async Task<int> DeleteAllAsync()
    {
        var count = await _context.EmployeeRecords.CountAsync();
        _context.EmployeeRecords.RemoveRange(_context.EmployeeRecords);
        await _context.SaveChangesAsync();
        return count;
    }
}
