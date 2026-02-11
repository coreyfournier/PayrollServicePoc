using PayrollService.Domain.Enums;
using PayrollService.Infrastructure.Orleans.State;

namespace PayrollService.Infrastructure.Orleans.Grains;

public interface IEmployeeGrain : IGrainWithGuidKey
{
    Task<EmployeeState?> GetAsync();
    Task<EmployeeState> CreateAsync(string firstName, string lastName, string email,
        PayType payType, decimal payRate, DateTime hireDate);
    Task UpdateAsync(string firstName, string lastName, string email,
        PayType payType, decimal payRate);
    Task DeactivateAsync();
    Task ActivateAsync();
    Task DeleteAsync();
}
