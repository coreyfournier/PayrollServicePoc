using PayrollService.Domain.Enums;
using PayrollService.Infrastructure.Orleans.State;

namespace PayrollService.Infrastructure.Orleans.Grains;

public interface IDeductionGrain : IGrainWithGuidKey
{
    Task<DeductionState?> GetAsync();
    Task<DeductionState> CreateAsync(
        Guid employeeId,
        DeductionType deductionType,
        string description,
        decimal amount,
        bool isPercentage);
    Task UpdateAsync(
        DeductionType deductionType,
        string description,
        decimal amount,
        bool isPercentage);
    Task DeactivateAsync();
    Task DeleteAsync();
}
