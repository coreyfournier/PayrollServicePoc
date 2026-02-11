namespace PayrollService.Infrastructure.Orleans.Grains;

public interface IEntityIndexGrain : IGrainWithStringKey
{
    Task<IReadOnlyList<Guid>> GetAllAsync();
    Task AddAsync(Guid entityId);
    Task RemoveAsync(Guid entityId);
}
