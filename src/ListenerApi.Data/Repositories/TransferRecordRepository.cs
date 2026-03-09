using ListenerApi.Data.DbContext;
using ListenerApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ListenerApi.Data.Repositories;

public class TransferRecordRepository : ITransferRecordRepository
{
    private readonly ListenerDbContext _context;

    public TransferRecordRepository(ListenerDbContext context)
    {
        _context = context;
    }

    public async Task<TransferRecord?> GetByIdAsync(Guid id)
        => await _context.TransferRecords.FindAsync(id);

    public async Task<List<TransferRecord>> GetByEmployeeIdAsync(Guid employeeId)
        => await _context.TransferRecords
            .Where(t => t.EmployeeId == employeeId)
            .OrderByDescending(t => t.InitiatedAt)
            .ToListAsync();

    public async Task<List<TransferRecord>> GetByEmployeeAndPayPeriodAsync(Guid employeeId, long payPeriodNumber)
        => await _context.TransferRecords
            .Where(t => t.EmployeeId == employeeId && t.PayPeriodNumber == payPeriodNumber && t.Status != "Failed")
            .ToListAsync();

    public async Task<int> GetCountByEmployeeAndDateAsync(Guid employeeId, DateTime sinceUtc)
        => await _context.TransferRecords
            .Where(t => t.EmployeeId == employeeId && t.InitiatedAt >= sinceUtc && t.Status != "Failed")
            .CountAsync();

    public async Task AddAsync(TransferRecord record)
    {
        _context.TransferRecords.Add(record);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(TransferRecord record)
    {
        _context.TransferRecords.Update(record);
        await _context.SaveChangesAsync();
    }
}
