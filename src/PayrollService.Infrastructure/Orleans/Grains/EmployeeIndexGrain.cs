using Orleans.Runtime;
using PayrollService.Infrastructure.Orleans.State;

namespace PayrollService.Infrastructure.Orleans.Grains;

public class EmployeeIndexGrain : Grain, IEmployeeIndexGrain
{
    private readonly IPersistentState<IndexState> _state;

    public EmployeeIndexGrain(
        [PersistentState("employeeindex", "MongoDBStore")] IPersistentState<IndexState> state)
    {
        _state = state;
    }

    public Task<IReadOnlyList<Guid>> GetAllAsync()
    {
        return Task.FromResult<IReadOnlyList<Guid>>(_state.State.EntityIds.ToList());
    }

    public async Task AddAsync(Guid employeeId)
    {
        if (_state.State.EntityIds.Add(employeeId))
        {
            await _state.WriteStateAsync();
        }
    }

    public async Task RemoveAsync(Guid employeeId)
    {
        if (_state.State.EntityIds.Remove(employeeId))
        {
            await _state.WriteStateAsync();
        }
    }
}
