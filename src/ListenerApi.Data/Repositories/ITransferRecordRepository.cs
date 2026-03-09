using ListenerApi.Data.Entities;

namespace ListenerApi.Data.Repositories;

public interface ITransferRecordRepository
{
    Task<TransferRecord?> GetByIdAsync(Guid id);
    Task<List<TransferRecord>> GetByEmployeeIdAsync(Guid employeeId);
    Task<List<TransferRecord>> GetByEmployeeAndPayPeriodAsync(Guid employeeId, long payPeriodNumber);
    Task<int> GetCountByEmployeeAndDateAsync(Guid employeeId, DateTime sinceUtc);
    Task AddAsync(TransferRecord record);
    Task UpdateAsync(TransferRecord record);
}
