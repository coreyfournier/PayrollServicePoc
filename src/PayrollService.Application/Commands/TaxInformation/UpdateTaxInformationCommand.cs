using MediatR;
using PayrollService.Application.DTOs;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Commands.TaxInformation;

public record UpdateTaxInformationCommand(
    Guid EmployeeId,
    string FederalFilingStatus,
    int FederalAllowances,
    decimal AdditionalFederalWithholding,
    string State,
    string StateFilingStatus,
    int StateAllowances,
    decimal AdditionalStateWithholding) : IRequest<TaxInformationDto>;

public class UpdateTaxInformationCommandHandler : IRequestHandler<UpdateTaxInformationCommand, TaxInformationDto>
{
    private readonly ITaxInformationRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateTaxInformationCommandHandler(ITaxInformationRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<TaxInformationDto> Handle(UpdateTaxInformationCommand request, CancellationToken cancellationToken)
    {
        var taxInfo = await _repository.GetByEmployeeIdAsync(request.EmployeeId, cancellationToken)
            ?? throw new KeyNotFoundException($"Tax information for employee {request.EmployeeId} not found.");

        taxInfo.Update(
            request.FederalFilingStatus,
            request.FederalAllowances,
            request.AdditionalFederalWithholding,
            request.State,
            request.StateFilingStatus,
            request.StateAllowances,
            request.AdditionalStateWithholding);

        await _unitOfWork.ExecuteAsync(
            async () => await _repository.UpdateAsync(taxInfo, cancellationToken),
            taxInfo,
            cancellationToken);

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
