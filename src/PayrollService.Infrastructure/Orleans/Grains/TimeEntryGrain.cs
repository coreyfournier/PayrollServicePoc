using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using PayrollService.Domain.Events;
using PayrollService.Infrastructure.Orleans.Events;
using PayrollService.Infrastructure.Orleans.State;

namespace PayrollService.Infrastructure.Orleans.Grains;

public class TimeEntryGrain : Grain, ITimeEntryGrain
{
    private readonly IPersistentState<TimeEntryState> _state;
    private readonly IKafkaEventPublisher _eventPublisher;
    private readonly ILogger<TimeEntryGrain> _logger;

    public TimeEntryGrain(
        [PersistentState("timeentry", "MongoDBStore")] IPersistentState<TimeEntryState> state,
        IKafkaEventPublisher eventPublisher,
        ILogger<TimeEntryGrain> logger)
    {
        _state = state;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public Task<TimeEntryState?> GetAsync()
    {
        return Task.FromResult(_state.State.IsInitialized ? _state.State : null);
    }

    public async Task<TimeEntryState> ClockInAsync(Guid employeeId)
    {
        var id = this.GetPrimaryKey();
        var now = DateTime.UtcNow;

        _state.State = new TimeEntryState
        {
            Id = id,
            EmployeeId = employeeId,
            ClockIn = now,
            ClockOut = null,
            HoursWorked = 0,
            CreatedAt = now,
            UpdatedAt = now,
            IsInitialized = true
        };

        await _state.WriteStateAsync();

        var domainEvent = new EmployeeClockedInEvent(id, employeeId, now);
        await _eventPublisher.PublishAsync("timeentry-events", domainEvent);

        var index = GrainFactory.GetGrain<IEntityIndexGrain>($"timeentry-employee-{employeeId}");
        await index.AddAsync(id);

        return _state.State;
    }

    public async Task<TimeEntryState> ClockOutAsync()
    {
        if (!_state.State.IsInitialized)
            throw new InvalidOperationException("Time entry does not exist.");

        if (_state.State.ClockOut.HasValue)
            throw new InvalidOperationException("Employee has already clocked out for this entry.");

        var clockOutTime = DateTime.UtcNow;
        _state.State.ClockOut = clockOutTime;
        _state.State.HoursWorked = Math.Round((decimal)(clockOutTime - _state.State.ClockIn).TotalHours, 2);
        _state.State.UpdatedAt = clockOutTime;

        await _state.WriteStateAsync();

        var domainEvent = new EmployeeClockedOutEvent(
            _state.State.Id,
            _state.State.EmployeeId,
            _state.State.ClockIn,
            clockOutTime,
            _state.State.HoursWorked);
        await _eventPublisher.PublishAsync("timeentry-events", domainEvent);

        return _state.State;
    }
}
