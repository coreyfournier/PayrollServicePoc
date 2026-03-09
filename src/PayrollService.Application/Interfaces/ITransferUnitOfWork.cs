using PayrollService.Domain.Common;

namespace PayrollService.Application.Interfaces;

/// <summary>
/// Unit of work for transfer operations, targeting the transfer-specific Dapr state store.
/// </summary>
public interface ITransferUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Entity entity, CancellationToken cancellationToken = default);
    Task ExecuteAsync(Func<Task> operation, Entity entity, CancellationToken cancellationToken = default);
}
