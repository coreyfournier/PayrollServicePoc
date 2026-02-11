using PayrollService.Domain.Entities;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Orleans.Grains;
using PayrollService.Infrastructure.Orleans.State;

namespace PayrollService.Infrastructure.Orleans.Repositories;

public class OrleansTimeEntryRepository : ITimeEntryRepository
{
    private readonly IGrainFactory _grainFactory;

    public OrleansTimeEntryRepository(IGrainFactory grainFactory)
    {
        _grainFactory = grainFactory;
    }

    public async Task<TimeEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var grain = _grainFactory.GetGrain<ITimeEntryGrain>(id);
        var state = await grain.GetAsync();
        return state == null ? null : MapToEntity(state);
    }

    public async Task<IEnumerable<TimeEntry>> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var index = _grainFactory.GetGrain<IEntityIndexGrain>($"timeentry-employee-{employeeId}");
        var ids = await index.GetAllAsync();
        var tasks = ids.Select(id => GetByIdAsync(id, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results
            .Where(e => e != null)
            .Cast<TimeEntry>()
            .OrderByDescending(t => t.ClockIn);
    }

    public async Task<TimeEntry?> GetActiveEntryByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var entries = await GetByEmployeeIdAsync(employeeId, cancellationToken);
        return entries.FirstOrDefault(t => t.ClockOut == null);
    }

    public async Task<TimeEntry> AddAsync(TimeEntry timeEntry, CancellationToken cancellationToken = default)
    {
        var grain = _grainFactory.GetGrain<ITimeEntryGrain>(timeEntry.Id);
        var state = await grain.ClockInAsync(timeEntry.EmployeeId);

        timeEntry.ClearDomainEvents();
        return MapToEntity(state);
    }

    public async Task UpdateAsync(TimeEntry timeEntry, CancellationToken cancellationToken = default)
    {
        var grain = _grainFactory.GetGrain<ITimeEntryGrain>(timeEntry.Id);

        if (timeEntry.ClockOut.HasValue)
        {
            await grain.ClockOutAsync();
        }

        timeEntry.ClearDomainEvents();
    }

    private static TimeEntry MapToEntity(TimeEntryState state)
    {
        var timeEntry = TimeEntry.ClockInEmployee(state.EmployeeId);

        SetPrivateProperty(timeEntry, nameof(TimeEntry.Id), state.Id);
        SetPrivateProperty(timeEntry, nameof(TimeEntry.ClockIn), state.ClockIn);
        SetPrivateProperty(timeEntry, nameof(TimeEntry.ClockOut), state.ClockOut);
        SetPrivateProperty(timeEntry, nameof(TimeEntry.HoursWorked), state.HoursWorked);
        SetPrivateProperty(timeEntry, nameof(TimeEntry.CreatedAt), state.CreatedAt);
        SetPrivateProperty(timeEntry, nameof(TimeEntry.UpdatedAt), state.UpdatedAt);

        timeEntry.ClearDomainEvents();
        return timeEntry;
    }

    private static void SetPrivateProperty(object obj, string propertyName, object? value)
    {
        var property = obj.GetType().GetProperty(propertyName);
        property?.SetValue(obj, value);
    }
}
