using ListenerApi.Data.Entities;

namespace ListenerApi.Data.Repositories;

public interface IEmployeeTransferStatusRepository
{
    Task<EmployeeTransferStatus?> GetByEmployeeIdAsync(Guid employeeId);
    Task UpsertAsync(EmployeeTransferStatus status);
}
