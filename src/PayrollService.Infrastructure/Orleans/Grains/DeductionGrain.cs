using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Events;
using PayrollService.Infrastructure.Orleans.Events;
using PayrollService.Infrastructure.Orleans.State;

namespace PayrollService.Infrastructure.Orleans.Grains;

public class DeductionGrain : Grain, IDeductionGrain
{
    private readonly IPersistentState<DeductionState> _state;
    private readonly IKafkaEventPublisher _eventPublisher;
    private readonly ILogger<DeductionGrain> _logger;

    public DeductionGrain(
        [PersistentState("deduction", "MongoDBStore")] IPersistentState<DeductionState> state,
        IKafkaEventPublisher eventPublisher,
        ILogger<DeductionGrain> logger)
    {
        _state = state;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public Task<DeductionState?> GetAsync()
    {
        return Task.FromResult(_state.State.IsInitialized ? _state.State : null);
    }

    public async Task<DeductionState> CreateAsync(
        Guid employeeId,
        DeductionType deductionType,
        string description,
        decimal amount,
        bool isPercentage)
    {
        var id = this.GetPrimaryKey();
        var now = DateTime.UtcNow;

        _state.State = new DeductionState
        {
            Id = id,
            EmployeeId = employeeId,
            DeductionType = deductionType,
            Description = description,
            Amount = amount,
            IsPercentage = isPercentage,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsInitialized = true
        };

        await _state.WriteStateAsync();

        var domainEvent = new DeductionCreatedEvent(id, employeeId, deductionType, description, amount, isPercentage);
        await _eventPublisher.PublishAsync("deduction-events", domainEvent);

        var index = GrainFactory.GetGrain<IEntityIndexGrain>($"deduction-employee-{employeeId}");
        await index.AddAsync(id);

        return _state.State;
    }

    public async Task UpdateAsync(
        DeductionType deductionType,
        string description,
        decimal amount,
        bool isPercentage)
    {
        if (!_state.State.IsInitialized)
            throw new InvalidOperationException("Deduction does not exist.");

        _state.State.DeductionType = deductionType;
        _state.State.Description = description;
        _state.State.Amount = amount;
        _state.State.IsPercentage = isPercentage;
        _state.State.UpdatedAt = DateTime.UtcNow;

        await _state.WriteStateAsync();

        var domainEvent = new DeductionUpdatedEvent(
            _state.State.Id,
            _state.State.EmployeeId,
            deductionType,
            description,
            amount,
            isPercentage);
        await _eventPublisher.PublishAsync("deduction-events", domainEvent);
    }

    public async Task DeactivateAsync()
    {
        if (!_state.State.IsInitialized)
            throw new InvalidOperationException("Deduction does not exist.");

        _state.State.IsActive = false;
        _state.State.UpdatedAt = DateTime.UtcNow;

        await _state.WriteStateAsync();

        var domainEvent = new DeductionDeactivatedEvent(_state.State.Id, _state.State.EmployeeId);
        await _eventPublisher.PublishAsync("deduction-events", domainEvent);
    }

    public async Task DeleteAsync()
    {
        if (!_state.State.IsInitialized)
            return;

        var id = _state.State.Id;
        var employeeId = _state.State.EmployeeId;

        await _state.ClearStateAsync();

        var index = GrainFactory.GetGrain<IEntityIndexGrain>($"deduction-employee-{employeeId}");
        await index.RemoveAsync(id);
    }
}
