using TransferService.Domain.Entities;

namespace TransferService.Domain.Repositories;

public interface IEmployeeTransferLimitsRepository
{
    Task<EmployeeTransferLimits?> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task UpsertAsync(EmployeeTransferLimits limits, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid employeeId, CancellationToken cancellationToken = default);
}
