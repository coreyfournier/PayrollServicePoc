using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Events;
using PayrollService.Infrastructure.Orleans.Events;
using PayrollService.Infrastructure.Orleans.State;

namespace PayrollService.Infrastructure.Orleans.Grains;

public class EmployeeGrain : Grain, IEmployeeGrain
{
    private readonly IPersistentState<EmployeeState> _state;
    private readonly IKafkaEventPublisher _eventPublisher;
    private readonly ILogger<EmployeeGrain> _logger;

    public EmployeeGrain(
        [PersistentState("employee", "MongoDBStore")] IPersistentState<EmployeeState> state,
        IKafkaEventPublisher eventPublisher,
        ILogger<EmployeeGrain> logger)
    {
        _state = state;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public Task<EmployeeState?> GetAsync()
    {
        return Task.FromResult(_state.State.IsInitialized ? _state.State : null);
    }

    public async Task<EmployeeState> CreateAsync(string firstName, string lastName, string email,
        PayType payType, decimal payRate, DateTime hireDate)
    {
        var id = this.GetPrimaryKey();
        var now = DateTime.UtcNow;

        _state.State = new EmployeeState
        {
            Id = id,
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            PayType = payType,
            PayRate = payRate,
            HireDate = hireDate,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsInitialized = true
        };

        await _state.WriteStateAsync();

        var domainEvent = new EmployeeCreatedEvent(id, firstName, lastName, email);
        await _eventPublisher.PublishAsync("employee-events", domainEvent);

        var index = GrainFactory.GetGrain<IEmployeeIndexGrain>(Guid.Empty);
        await index.AddAsync(id);

        return _state.State;
    }

    public async Task UpdateAsync(string firstName, string lastName, string email,
        PayType payType, decimal payRate)
    {
        if (!_state.State.IsInitialized)
            throw new InvalidOperationException("Employee does not exist.");

        _state.State.FirstName = firstName;
        _state.State.LastName = lastName;
        _state.State.Email = email;
        _state.State.PayType = payType;
        _state.State.PayRate = payRate;
        _state.State.UpdatedAt = DateTime.UtcNow;

        await _state.WriteStateAsync();

        var domainEvent = new EmployeeUpdatedEvent(_state.State.Id, firstName, lastName, email, payType, payRate);
        await _eventPublisher.PublishAsync("employee-events", domainEvent);
    }

    public async Task DeactivateAsync()
    {
        if (!_state.State.IsInitialized)
            throw new InvalidOperationException("Employee does not exist.");

        _state.State.IsActive = false;
        _state.State.UpdatedAt = DateTime.UtcNow;

        await _state.WriteStateAsync();

        var domainEvent = new EmployeeDeactivatedEvent(_state.State.Id);
        await _eventPublisher.PublishAsync("employee-events", domainEvent);
    }

    public async Task ActivateAsync()
    {
        if (!_state.State.IsInitialized)
            throw new InvalidOperationException("Employee does not exist.");

        _state.State.IsActive = true;
        _state.State.UpdatedAt = DateTime.UtcNow;

        await _state.WriteStateAsync();

        var domainEvent = new EmployeeActivatedEvent(_state.State.Id);
        await _eventPublisher.PublishAsync("employee-events", domainEvent);
    }

    public async Task DeleteAsync()
    {
        if (!_state.State.IsInitialized)
            return;

        var id = _state.State.Id;

        await _state.ClearStateAsync();

        var index = GrainFactory.GetGrain<IEmployeeIndexGrain>(Guid.Empty);
        await index.RemoveAsync(id);
    }
}
