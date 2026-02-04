using PayrollService.Domain.Entities;

namespace PayrollService.Domain.Repositories;

public interface IDeductionRepository
{
    Task<Deduction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Deduction>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Deduction> AddAsync(Deduction deduction, CancellationToken cancellationToken = default);
    Task UpdateAsync(Deduction deduction, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
