using TransferService.Domain.Entities;

namespace TransferService.Domain.Repositories;

public interface ITransferRepository
{
    Task<Transfer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Transfer>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Transfer>> GetByEmployeeAndPayPeriodAsync(Guid employeeId, long payPeriodNumber, CancellationToken cancellationToken = default);
    Task<int> GetCountByEmployeeAndDateAsync(Guid employeeId, DateTime date, CancellationToken cancellationToken = default);
    Task<Transfer> AddAsync(Transfer transfer, CancellationToken cancellationToken = default);
    Task UpdateAsync(Transfer transfer, CancellationToken cancellationToken = default);
}
