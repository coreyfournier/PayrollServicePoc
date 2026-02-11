using Orleans.Runtime;
using PayrollService.Infrastructure.Orleans.State;

namespace PayrollService.Infrastructure.Orleans.Grains;

public class EntityIndexGrain : Grain, IEntityIndexGrain
{
    private readonly IPersistentState<IndexState> _state;

    public EntityIndexGrain(
        [PersistentState("entityindex", "MongoDBStore")] IPersistentState<IndexState> state)
    {
        _state = state;
    }

    public Task<IReadOnlyList<Guid>> GetAllAsync()
    {
        return Task.FromResult<IReadOnlyList<Guid>>(_state.State.EntityIds.ToList());
    }

    public async Task AddAsync(Guid entityId)
    {
        if (_state.State.EntityIds.Add(entityId))
        {
            await _state.WriteStateAsync();
        }
    }

    public async Task RemoveAsync(Guid entityId)
    {
        if (_state.State.EntityIds.Remove(entityId))
        {
            await _state.WriteStateAsync();
        }
    }
}
