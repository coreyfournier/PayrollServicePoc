using MongoDB.Driver;
using TransferService.Domain.Entities;
using TransferService.Domain.Enums;
using TransferService.Domain.Exceptions;
using TransferService.Domain.Repositories;
using TransferService.Infrastructure.Persistence;

namespace TransferService.Infrastructure.Repositories;

public class TransferRepository : ITransferRepository
{
    private readonly TransferMongoDbContext _dbContext;

    public TransferRepository(TransferMongoDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Transfer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Transfers.Find(t => t.Id == id).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<Transfer>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Transfers
            .Find(t => t.EmployeeId == employeeId)
            .SortByDescending(t => t.InitiatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Transfer>> GetByEmployeeAndPayPeriodAsync(Guid employeeId, long payPeriodNumber, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Transfers
            .Find(t => t.EmployeeId == employeeId && t.PayPeriodNumber == payPeriodNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountByEmployeeAndDateAsync(Guid employeeId, DateTime date, CancellationToken cancellationToken = default)
    {
        var nextDay = date.AddDays(1);
        return (int)await _dbContext.Transfers
            .CountDocumentsAsync(t => t.EmployeeId == employeeId
                && t.Status == TransferStatus.Completed
                && t.InitiatedAt >= date && t.InitiatedAt < nextDay, cancellationToken: cancellationToken);
    }

    public async Task<IEnumerable<Transfer>> GetRecentAsync(int limit = 50, string? statusFilter = null, CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<Transfer>.Filter;
        var filter = filterBuilder.Empty;

        if (!string.IsNullOrEmpty(statusFilter))
        {
            var status = Enum.Parse<TransferStatus>(statusFilter, ignoreCase: true);
            filter = filterBuilder.Eq(t => t.Status, status);
        }

        return await _dbContext.Transfers
            .Find(filter)
            .SortByDescending(t => t.InitiatedAt)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasInProgressTransferAsync(Guid employeeId, Guid? excludeTransferId = null, CancellationToken cancellationToken = default)
    {
        var inProgressStatuses = new[] { TransferStatus.Initiated, TransferStatus.Processing, TransferStatus.AwaitingConfirmation };
        var builder = Builders<Transfer>.Filter;
        var filter = builder.Eq(t => t.EmployeeId, employeeId) & builder.In(t => t.Status, inProgressStatuses);
        if (excludeTransferId.HasValue)
            filter &= builder.Ne(t => t.Id, excludeTransferId.Value);
        var count = await _dbContext.Transfers.CountDocumentsAsync(filter, new CountOptions { Limit = 1 }, cancellationToken);
        return count > 0;
    }

    public async Task<Transfer> AddAsync(Transfer transfer, CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.Transfers.ReplaceOneAsync(
                t => t.Id == transfer.Id,
                transfer,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);
            return transfer;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Code == 11000 && ex.WriteError.Message.Contains("unique_employee_in_progress_transfer"))
        {
            throw new DuplicateInProgressTransferException(transfer.EmployeeId);
        }
    }

    public async Task UpdateAsync(Transfer transfer, CancellationToken cancellationToken = default)
    {
        await _dbContext.Transfers.ReplaceOneAsync(
            t => t.Id == transfer.Id,
            transfer,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }
}
