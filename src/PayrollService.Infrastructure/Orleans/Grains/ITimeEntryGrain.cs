using PayrollService.Infrastructure.Orleans.State;

namespace PayrollService.Infrastructure.Orleans.Grains;

public interface ITimeEntryGrain : IGrainWithGuidKey
{
    Task<TimeEntryState?> GetAsync();
    Task<TimeEntryState> ClockInAsync(Guid employeeId);
    Task<TimeEntryState> ClockOutAsync();
}
