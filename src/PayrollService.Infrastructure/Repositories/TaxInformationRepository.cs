using MongoDB.Driver;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Persistence;

namespace PayrollService.Infrastructure.Repositories;

public class TaxInformationRepository : ITaxInformationRepository
{
    private readonly MongoDbContext _context;

    public TaxInformationRepository(MongoDbContext context)
    {
        _context = context;
    }

    public async Task<TaxInformation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.TaxInformation
            .Find(t => t.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<TaxInformation?> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        return await _context.TaxInformation
            .Find(t => t.EmployeeId == employeeId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<TaxInformation> AddAsync(TaxInformation taxInformation, CancellationToken cancellationToken = default)
    {
        await _context.TaxInformation.InsertOneAsync(taxInformation, cancellationToken: cancellationToken);
        return taxInformation;
    }

    public async Task UpdateAsync(TaxInformation taxInformation, CancellationToken cancellationToken = default)
    {
        await _context.TaxInformation.ReplaceOneAsync(
            t => t.Id == taxInformation.Id,
            taxInformation,
            cancellationToken: cancellationToken);
    }
}
