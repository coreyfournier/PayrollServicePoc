using PayrollService.Domain.Entities;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Orleans.Grains;
using PayrollService.Infrastructure.Orleans.State;

namespace PayrollService.Infrastructure.Orleans.Repositories;

public class OrleansEmployeeRepository : IEmployeeRepository
{
    private readonly IGrainFactory _grainFactory;

    public OrleansEmployeeRepository(IGrainFactory grainFactory)
    {
        _grainFactory = grainFactory;
    }

    public async Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var grain = _grainFactory.GetGrain<IEmployeeGrain>(id);
        var state = await grain.GetAsync();
        return state == null ? null : MapToEntity(state);
    }

    public async Task<IEnumerable<Employee>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var index = _grainFactory.GetGrain<IEmployeeIndexGrain>(Guid.Empty);
        var ids = await index.GetAllAsync();
        var tasks = ids.Select(id => GetByIdAsync(id, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.Where(e => e != null).Cast<Employee>();
    }

    public async Task<Employee> AddAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        var grain = _grainFactory.GetGrain<IEmployeeGrain>(employee.Id);
        var state = await grain.CreateAsync(
            employee.FirstName,
            employee.LastName,
            employee.Email,
            employee.PayType,
            employee.PayRate,
            employee.HireDate);

        employee.ClearDomainEvents();
        return MapToEntity(state);
    }

    public async Task UpdateAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        var grain = _grainFactory.GetGrain<IEmployeeGrain>(employee.Id);

        if (!employee.IsActive)
        {
            await grain.DeactivateAsync();
        }
        else
        {
            var currentState = await grain.GetAsync();
            if (currentState != null && !currentState.IsActive && employee.IsActive)
            {
                await grain.ActivateAsync();
            }

            await grain.UpdateAsync(
                employee.FirstName,
                employee.LastName,
                employee.Email,
                employee.PayType,
                employee.PayRate);
        }

        employee.ClearDomainEvents();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var grain = _grainFactory.GetGrain<IEmployeeGrain>(id);
        await grain.DeleteAsync();
    }

    private static Employee MapToEntity(EmployeeState state)
    {
        var employee = Employee.Create(
            state.FirstName,
            state.LastName,
            state.Email,
            state.PayType,
            state.PayRate,
            state.HireDate);

        SetPrivateProperty(employee, nameof(Employee.Id), state.Id);
        SetPrivateProperty(employee, nameof(Employee.CreatedAt), state.CreatedAt);
        SetPrivateProperty(employee, nameof(Employee.UpdatedAt), state.UpdatedAt);
        SetPrivateProperty(employee, nameof(Employee.IsActive), state.IsActive);

        employee.ClearDomainEvents();
        return employee;
    }

    private static void SetPrivateProperty(object obj, string propertyName, object value)
    {
        var property = obj.GetType().GetProperty(propertyName);
        property?.SetValue(obj, value);
    }
}
