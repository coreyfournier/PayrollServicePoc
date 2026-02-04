using MediatR;
using PayrollService.Application.DTOs;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Commands.Deduction;

public record CreateDeductionCommand(
    Guid EmployeeId,
    DeductionType DeductionType,
    string Description,
    decimal Amount,
    bool IsPercentage) : IRequest<DeductionDto>;

public class CreateDeductionCommandHandler : IRequestHandler<CreateDeductionCommand, DeductionDto>
{
    private readonly IDeductionRepository _repository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateDeductionCommandHandler(
        IDeductionRepository repository,
        IEmployeeRepository employeeRepository,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _employeeRepository = employeeRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<DeductionDto> Handle(CreateDeductionCommand request, CancellationToken cancellationToken)
    {
        var employee = await _employeeRepository.GetByIdAsync(request.EmployeeId, cancellationToken)
            ?? throw new KeyNotFoundException($"Employee with ID {request.EmployeeId} not found.");

        var deduction = Domain.Entities.Deduction.Create(
            request.EmployeeId,
            request.DeductionType,
            request.Description,
            request.Amount,
            request.IsPercentage);

        var result = await _unitOfWork.ExecuteAsync(
            async () => await _repository.AddAsync(deduction, cancellationToken),
            deduction,
            cancellationToken);

        return new DeductionDto(
            result.Id,
            result.EmployeeId,
            result.DeductionType,
            result.Description,
            result.Amount,
            result.IsPercentage,
            result.IsActive,
            result.CreatedAt,
            result.UpdatedAt);
    }
}
