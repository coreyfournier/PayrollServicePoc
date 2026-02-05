using HotChocolate.Data;
using ListenerApi.Data.DbContext;
using ListenerApi.Data.Entities;

namespace ListenerApi.GraphQL.Queries;

public class EmployeeQuery
{
    [UseFiltering]
    [UseSorting]
    public IQueryable<EmployeeRecord> GetEmployees([Service] ListenerDbContext context)
        => context.EmployeeRecords;

    public async Task<EmployeeRecord?> GetEmployeeById(
        Guid id,
        [Service] ListenerDbContext context)
        => await context.EmployeeRecords.FindAsync(id);
}
