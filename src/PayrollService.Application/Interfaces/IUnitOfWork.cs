using PayrollService.Domain.Common;

namespace PayrollService.Application.Interfaces;

public interface IUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Entity entity, CancellationToken cancellationToken = default);
    Task ExecuteAsync(Func<Task> operation, Entity entity, CancellationToken cancellationToken = default);
}
