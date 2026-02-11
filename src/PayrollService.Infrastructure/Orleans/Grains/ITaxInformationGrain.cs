using PayrollService.Infrastructure.Orleans.State;

namespace PayrollService.Infrastructure.Orleans.Grains;

public interface ITaxInformationGrain : IGrainWithGuidKey
{
    Task<TaxInformationState?> GetAsync();
    Task<TaxInformationState> CreateAsync(
        Guid employeeId,
        string federalFilingStatus,
        int federalAllowances,
        decimal additionalFederalWithholding,
        string state,
        string stateFilingStatus,
        int stateAllowances,
        decimal additionalStateWithholding);
    Task UpdateAsync(
        string federalFilingStatus,
        int federalAllowances,
        decimal additionalFederalWithholding,
        string state,
        string stateFilingStatus,
        int stateAllowances,
        decimal additionalStateWithholding);
}
