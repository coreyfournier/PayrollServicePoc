namespace PayrollService.Infrastructure.Orleans.Grains;

public interface IEmployeeIndexGrain : IGrainWithGuidKey
{
    Task<IReadOnlyList<Guid>> GetAllAsync();
    Task AddAsync(Guid employeeId);
    Task RemoveAsync(Guid employeeId);
}
