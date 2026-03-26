using HotChocolate.Data;
using ListenerApi.Data.DbContext;
using ListenerApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ListenerApi.GraphQL.Queries;

[ExtendObjectType<EmployeeQuery>]
public class TransferQuery
{
    [UseFiltering]
    [UseSorting]
    public IQueryable<TransferRecord> GetTransfers([Service] ListenerDbContext context)
        => context.TransferRecords.Include(t => t.Employee);

    public async Task<List<TransferRecord>> GetTransfersByEmployeeId(
        Guid employeeId,
        [Service] ListenerDbContext context)
        => await context.TransferRecords
            .Where(t => t.EmployeeId == employeeId)
            .OrderByDescending(t => t.InitiatedAt)
            .ToListAsync();

    public async Task<EmployeeTransferStatus?> GetTransferStatus(
        Guid employeeId,
        [Service] ListenerDbContext context)
        => await context.EmployeeTransferStatuses.FindAsync(employeeId);
}
