using ListenerApi.Data.Entities;

namespace ListenerApi.Data.Repositories;

public interface IEmployeeRecordRepository
{
    Task<EmployeeRecord?> GetByIdAsync(Guid id);
    Task<IQueryable<EmployeeRecord>> GetAllAsync();
    Task AddAsync(EmployeeRecord record);
    Task UpdateAsync(EmployeeRecord record);
    Task<int> DeleteAllAsync();
}
