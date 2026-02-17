using ListenerApi.Data.Entities;

namespace ListenerApi.Data.Repositories;

public interface IEmployeePayAttributesRepository
{
    Task<EmployeePayAttributes?> GetByEmployeeIdAsync(Guid employeeId);
    Task UpsertAsync(EmployeePayAttributes payAttributes);
    Task DeleteByEmployeeIdAsync(Guid employeeId);
}
