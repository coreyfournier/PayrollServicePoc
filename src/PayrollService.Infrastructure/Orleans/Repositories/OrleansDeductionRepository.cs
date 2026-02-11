using PayrollService.Domain.Entities;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Orleans.Grains;
using PayrollService.Infrastructure.Orleans.State;

namespace PayrollService.Infrastructure.Orleans.Repositories;

public class OrleansDeductionRepository : IDeductionRepository
{
    private readonly IGrainFactory _grainFactory;

    public OrleansDeductionRepository(IGrainFactory grainFactory)
    {
        _grainFactory = grainFactory;
    }

    public async Task<Deduction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var grain = _grainFactory.GetGrain<IDeductionGrain>(id);
        var state = await grain.GetAsync();
        return state == null ? null : MapToEntity(state);
    }

    public async Task<IEnumerable<Deduction>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var index = _grainFactory.GetGrain<IEntityIndexGrain>($"deduction-employee-{employeeId}");
        var ids = await index.GetAllAsync();
        var tasks = ids.Select(id => GetByIdAsync(id, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.Where(d => d != null).Cast<Deduction>();
    }

    public async Task<Deduction> AddAsync(Deduction deduction, CancellationToken cancellationToken = default)
    {
        var grain = _grainFactory.GetGrain<IDeductionGrain>(deduction.Id);
        var state = await grain.CreateAsync(
            deduction.EmployeeId,
            deduction.DeductionType,
            deduction.Description,
            deduction.Amount,
            deduction.IsPercentage);

        deduction.ClearDomainEvents();
        return MapToEntity(state);
    }

    public async Task UpdateAsync(Deduction deduction, CancellationToken cancellationToken = default)
    {
        var grain = _grainFactory.GetGrain<IDeductionGrain>(deduction.Id);

        if (!deduction.IsActive)
        {
            await grain.DeactivateAsync();
        }
        else
        {
            await grain.UpdateAsync(
                deduction.DeductionType,
                deduction.Description,
                deduction.Amount,
                deduction.IsPercentage);
        }

        deduction.ClearDomainEvents();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var grain = _grainFactory.GetGrain<IDeductionGrain>(id);
        await grain.DeleteAsync();
    }

    private static Deduction MapToEntity(DeductionState state)
    {
        var deduction = Deduction.Create(
            state.EmployeeId,
            state.DeductionType,
            state.Description,
            state.Amount,
            state.IsPercentage);

        SetPrivateProperty(deduction, nameof(Deduction.Id), state.Id);
        SetPrivateProperty(deduction, nameof(Deduction.CreatedAt), state.CreatedAt);
        SetPrivateProperty(deduction, nameof(Deduction.UpdatedAt), state.UpdatedAt);
        SetPrivateProperty(deduction, nameof(Deduction.IsActive), state.IsActive);

        deduction.ClearDomainEvents();
        return deduction;
    }

    private static void SetPrivateProperty(object obj, string propertyName, object value)
    {
        var property = obj.GetType().GetProperty(propertyName);
        property?.SetValue(obj, value);
    }
}
