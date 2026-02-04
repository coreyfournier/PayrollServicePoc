using MediatR;
using PayrollService.Application.DTOs;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Commands.TaxInformation;

public record CreateTaxInformationCommand(
    Guid EmployeeId,
    string FederalFilingStatus,
    int FederalAllowances,
    decimal AdditionalFederalWithholding,
    string State,
    string StateFilingStatus,
    int StateAllowances,
    decimal AdditionalStateWithholding) : IRequest<TaxInformationDto>;

public class CreateTaxInformationCommandHandler : IRequestHandler<CreateTaxInformationCommand, TaxInformationDto>
{
    private readonly ITaxInformationRepository _repository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTaxInformationCommandHandler(
        ITaxInformationRepository repository,
        IEmployeeRepository employeeRepository,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _employeeRepository = employeeRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<TaxInformationDto> Handle(CreateTaxInformationCommand request, CancellationToken cancellationToken)
    {
        var employee = await _employeeRepository.GetByIdAsync(request.EmployeeId, cancellationToken)
            ?? throw new KeyNotFoundException($"Employee with ID {request.EmployeeId} not found.");

        var existing = await _repository.GetByEmployeeIdAsync(request.EmployeeId, cancellationToken);
        if (existing != null)
            throw new InvalidOperationException("Tax information already exists for this employee.");

        var taxInfo = Domain.Entities.TaxInformation.Create(
            request.EmployeeId,
            request.FederalFilingStatus,
            request.FederalAllowances,
            request.AdditionalFederalWithholding,
            request.State,
            request.StateFilingStatus,
            request.StateAllowances,
            request.AdditionalStateWithholding);

        var result = await _unitOfWork.ExecuteAsync(
            async () => await _repository.AddAsync(taxInfo, cancellationToken),
            taxInfo,
            cancellationToken);

        return new TaxInformationDto(
            result.Id,
            result.EmployeeId,
            result.FederalFilingStatus,
            result.FederalAllowances,
            result.AdditionalFederalWithholding,
            result.State,
            result.StateFilingStatus,
            result.StateAllowances,
            result.AdditionalStateWithholding,
            result.CreatedAt,
            result.UpdatedAt);
    }
}
