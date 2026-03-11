using MongoDB.Driver;
using TransferService.Domain.Entities;
using TransferService.Domain.Repositories;
using TransferService.Infrastructure.Persistence;

namespace TransferService.Infrastructure.Repositories;

public class EmployeeTransferLimitsRepository : IEmployeeTransferLimitsRepository
{
    private readonly TransferMongoDbContext _dbContext;

    public EmployeeTransferLimitsRepository(TransferMongoDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EmployeeTransferLimits?> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.EmployeeTransferLimits
            .Find(l => l.EmployeeId == employeeId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpsertAsync(EmployeeTransferLimits limits, CancellationToken cancellationToken = default)
    {
        await _dbContext.EmployeeTransferLimits.ReplaceOneAsync(
            l => l.EmployeeId == limits.EmployeeId,
            limits,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task DeleteAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        await _dbContext.EmployeeTransferLimits.DeleteOneAsync(
            l => l.EmployeeId == employeeId,
            cancellationToken);
    }
}
