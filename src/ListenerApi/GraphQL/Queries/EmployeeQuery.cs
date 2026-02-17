using HotChocolate.Data;
using ListenerApi.Data.DbContext;
using ListenerApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ListenerApi.GraphQL.Queries;

public class EmployeeQuery
{
    [UseFiltering]
    [UseSorting]
    public IQueryable<EmployeeRecord> GetEmployees([Service] ListenerDbContext context)
        => context.EmployeeRecords.Include(e => e.PayAttributes);

    public async Task<EmployeeRecord?> GetEmployeeById(
        Guid id,
        [Service] ListenerDbContext context)
        => await context.EmployeeRecords
            .Include(e => e.PayAttributes)
            .FirstOrDefaultAsync(e => e.Id == id);
}
