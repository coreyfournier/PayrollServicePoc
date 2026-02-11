using PayrollService.Application.Interfaces;
using PayrollService.Domain.Common;

namespace PayrollService.Infrastructure.Orleans;

/// <summary>
/// Orleans-based unit of work that simply executes operations.
/// Event publishing is handled by the grains directly via Orleans streams.
/// </summary>
public class OrleansUnitOfWork : IUnitOfWork
{
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        var result = await operation();
        entity.ClearDomainEvents();
        return result;
    }

    public async Task ExecuteAsync(Func<Task> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        await operation();
        entity.ClearDomainEvents();
    }
}
