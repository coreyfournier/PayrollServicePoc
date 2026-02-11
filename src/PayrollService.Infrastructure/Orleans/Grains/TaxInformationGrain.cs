using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using PayrollService.Domain.Events;
using PayrollService.Infrastructure.Orleans.Events;
using PayrollService.Infrastructure.Orleans.State;

namespace PayrollService.Infrastructure.Orleans.Grains;

public class TaxInformationGrain : Grain, ITaxInformationGrain
{
    private readonly IPersistentState<TaxInformationState> _state;
    private readonly IKafkaEventPublisher _eventPublisher;
    private readonly ILogger<TaxInformationGrain> _logger;

    public TaxInformationGrain(
        [PersistentState("taxinformation", "MongoDBStore")] IPersistentState<TaxInformationState> state,
        IKafkaEventPublisher eventPublisher,
        ILogger<TaxInformationGrain> logger)
    {
        _state = state;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public Task<TaxInformationState?> GetAsync()
    {
        return Task.FromResult(_state.State.IsInitialized ? _state.State : null);
    }

    public async Task<TaxInformationState> CreateAsync(
        Guid employeeId,
        string federalFilingStatus,
        int federalAllowances,
        decimal additionalFederalWithholding,
        string state,
        string stateFilingStatus,
        int stateAllowances,
        decimal additionalStateWithholding)
    {
        var id = this.GetPrimaryKey();
        var now = DateTime.UtcNow;

        _state.State = new TaxInformationState
        {
            Id = id,
            EmployeeId = employeeId,
            FederalFilingStatus = federalFilingStatus,
            FederalAllowances = federalAllowances,
            AdditionalFederalWithholding = additionalFederalWithholding,
            State = state,
            StateFilingStatus = stateFilingStatus,
            StateAllowances = stateAllowances,
            AdditionalStateWithholding = additionalStateWithholding,
            CreatedAt = now,
            UpdatedAt = now,
            IsInitialized = true
        };

        await _state.WriteStateAsync();

        var domainEvent = new TaxInformationCreatedEvent(
            id,
            employeeId,
            federalFilingStatus,
            federalAllowances,
            additionalFederalWithholding,
            state,
            stateFilingStatus,
            stateAllowances,
            additionalStateWithholding);
        await _eventPublisher.PublishAsync("taxinfo-events", domainEvent);

        var index = GrainFactory.GetGrain<IEntityIndexGrain>($"taxinfo-employee-{employeeId}");
        await index.AddAsync(id);

        return _state.State;
    }

    public async Task UpdateAsync(
        string federalFilingStatus,
        int federalAllowances,
        decimal additionalFederalWithholding,
        string state,
        string stateFilingStatus,
        int stateAllowances,
        decimal additionalStateWithholding)
    {
        if (!_state.State.IsInitialized)
            throw new InvalidOperationException("Tax information does not exist.");

        _state.State.FederalFilingStatus = federalFilingStatus;
        _state.State.FederalAllowances = federalAllowances;
        _state.State.AdditionalFederalWithholding = additionalFederalWithholding;
        _state.State.State = state;
        _state.State.StateFilingStatus = stateFilingStatus;
        _state.State.StateAllowances = stateAllowances;
        _state.State.AdditionalStateWithholding = additionalStateWithholding;
        _state.State.UpdatedAt = DateTime.UtcNow;

        await _state.WriteStateAsync();

        var domainEvent = new TaxInformationUpdatedEvent(
            _state.State.Id,
            _state.State.EmployeeId,
            federalFilingStatus,
            federalAllowances,
            additionalFederalWithholding,
            state,
            stateFilingStatus,
            stateAllowances,
            additionalStateWithholding);
        await _eventPublisher.PublishAsync("taxinfo-events", domainEvent);
    }
}
