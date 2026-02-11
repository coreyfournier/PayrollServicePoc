using PayrollService.Domain.Entities;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Orleans.Grains;
using PayrollService.Infrastructure.Orleans.State;

namespace PayrollService.Infrastructure.Orleans.Repositories;

public class OrleansTaxInformationRepository : ITaxInformationRepository
{
    private readonly IGrainFactory _grainFactory;

    public OrleansTaxInformationRepository(IGrainFactory grainFactory)
    {
        _grainFactory = grainFactory;
    }

    public async Task<TaxInformation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var grain = _grainFactory.GetGrain<ITaxInformationGrain>(id);
        var state = await grain.GetAsync();
        return state == null ? null : MapToEntity(state);
    }

    public async Task<TaxInformation?> GetByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var index = _grainFactory.GetGrain<IEntityIndexGrain>($"taxinfo-employee-{employeeId}");
        var ids = await index.GetAllAsync();

        if (ids.Count == 0)
            return null;

        return await GetByIdAsync(ids.First(), cancellationToken);
    }

    public async Task<TaxInformation> AddAsync(TaxInformation taxInformation, CancellationToken cancellationToken = default)
    {
        var grain = _grainFactory.GetGrain<ITaxInformationGrain>(taxInformation.Id);
        var state = await grain.CreateAsync(
            taxInformation.EmployeeId,
            taxInformation.FederalFilingStatus,
            taxInformation.FederalAllowances,
            taxInformation.AdditionalFederalWithholding,
            taxInformation.State,
            taxInformation.StateFilingStatus,
            taxInformation.StateAllowances,
            taxInformation.AdditionalStateWithholding);

        taxInformation.ClearDomainEvents();
        return MapToEntity(state);
    }

    public async Task UpdateAsync(TaxInformation taxInformation, CancellationToken cancellationToken = default)
    {
        var grain = _grainFactory.GetGrain<ITaxInformationGrain>(taxInformation.Id);

        await grain.UpdateAsync(
            taxInformation.FederalFilingStatus,
            taxInformation.FederalAllowances,
            taxInformation.AdditionalFederalWithholding,
            taxInformation.State,
            taxInformation.StateFilingStatus,
            taxInformation.StateAllowances,
            taxInformation.AdditionalStateWithholding);

        taxInformation.ClearDomainEvents();
    }

    private static TaxInformation MapToEntity(TaxInformationState state)
    {
        var taxInfo = TaxInformation.Create(
            state.EmployeeId,
            state.FederalFilingStatus,
            state.FederalAllowances,
            state.AdditionalFederalWithholding,
            state.State,
            state.StateFilingStatus,
            state.StateAllowances,
            state.AdditionalStateWithholding);

        SetPrivateProperty(taxInfo, nameof(TaxInformation.Id), state.Id);
        SetPrivateProperty(taxInfo, nameof(TaxInformation.CreatedAt), state.CreatedAt);
        SetPrivateProperty(taxInfo, nameof(TaxInformation.UpdatedAt), state.UpdatedAt);

        taxInfo.ClearDomainEvents();
        return taxInfo;
    }

    private static void SetPrivateProperty(object obj, string propertyName, object value)
    {
        var property = obj.GetType().GetProperty(propertyName);
        property?.SetValue(obj, value);
    }
}
