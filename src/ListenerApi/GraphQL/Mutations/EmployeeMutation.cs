using ListenerApi.Data.DbContext;
using Microsoft.EntityFrameworkCore;

namespace ListenerApi.GraphQL.Mutations;

public class EmployeeMutation
{
    public async Task<DeleteAllResult> DeleteAllEmployees([Service] ListenerDbContext context)
    {
        var count = await context.EmployeeRecords.CountAsync();
        context.EmployeeRecords.RemoveRange(context.EmployeeRecords);
        await context.SaveChangesAsync();

        return new DeleteAllResult(count, true, $"Successfully deleted {count} employee records");
    }
}

public record DeleteAllResult(int DeletedCount, bool Success, string Message);
