using MongoDB.Driver;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Persistence;

namespace PayrollService.Infrastructure.Repositories;

public class DeductionRepository : IDeductionRepository
{
    private readonly MongoDbContext _context;

    public DeductionRepository(MongoDbContext context)
    {
        _context = context;
    }

    public async Task<Deduction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Deductions
            .Find(d => d.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<Deduction>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        return await _context.Deductions
            .Find(d => d.EmployeeId == employeeId)
            .ToListAsync(cancellationToken);
    }

    public async Task<Deduction> AddAsync(Deduction deduction, CancellationToken cancellationToken = default)
    {
        await _context.Deductions.InsertOneAsync(deduction, cancellationToken: cancellationToken);
        return deduction;
    }

    public async Task UpdateAsync(Deduction deduction, CancellationToken cancellationToken = default)
    {
        await _context.Deductions.ReplaceOneAsync(
            d => d.Id == deduction.Id,
            deduction,
            cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _context.Deductions.DeleteOneAsync(d => d.Id == id, cancellationToken);
    }
}
