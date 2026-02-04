using MediatR;
using PayrollService.Application.DTOs;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Queries.TaxInformation;

public record GetTaxInformationByEmployeeQuery(Guid EmployeeId) : IRequest<TaxInformationDto?>;

public class GetTaxInformationByEmployeeQueryHandler : IRequestHandler<GetTaxInformationByEmployeeQuery, TaxInformationDto?>
{
    private readonly ITaxInformationRepository _repository;

    public GetTaxInformationByEmployeeQueryHandler(ITaxInformationRepository repository)
    {
        _repository = repository;
    }

    public async Task<TaxInformationDto?> Handle(GetTaxInformationByEmployeeQuery request, CancellationToken cancellationToken)
    {
        var taxInfo = await _repository.GetByEmployeeIdAsync(request.EmployeeId, cancellationToken);

        if (taxInfo == null)
            return null;

        return new TaxInformationDto(
            taxInfo.Id,
            taxInfo.EmployeeId,
            taxInfo.FederalFilingStatus,
            taxInfo.FederalAllowances,
            taxInfo.AdditionalFederalWithholding,
            taxInfo.State,
            taxInfo.StateFilingStatus,
            taxInfo.StateAllowances,
            taxInfo.AdditionalStateWithholding,
            taxInfo.CreatedAt,
            taxInfo.UpdatedAt);
    }
}
