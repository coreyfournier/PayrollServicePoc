using PayrollService.Domain.Entities;

namespace PayrollService.Domain.Repositories;

public interface ITaxInformationRepository
{
    Task<TaxInformation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TaxInformation?> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<TaxInformation> AddAsync(TaxInformation taxInformation, CancellationToken cancellationToken = default);
    Task UpdateAsync(TaxInformation taxInformation, CancellationToken cancellationToken = default);
}
