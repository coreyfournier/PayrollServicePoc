using MediatR;
using PayrollService.Application.DTOs;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Commands.Deduction;

public record UpdateDeductionCommand(
    Guid Id,
    DeductionType DeductionType,
    string Description,
    decimal Amount,
    bool IsPercentage) : IRequest<DeductionDto>;

public class UpdateDeductionCommandHandler : IRequestHandler<UpdateDeductionCommand, DeductionDto>
{
    private readonly IDeductionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateDeductionCommandHandler(IDeductionRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<DeductionDto> Handle(UpdateDeductionCommand request, CancellationToken cancellationToken)
    {
        var deduction = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Deduction with ID {request.Id} not found.");

        deduction.Update(request.DeductionType, request.Description, request.Amount, request.IsPercentage);

        await _unitOfWork.ExecuteAsync(
            async () => await _repository.UpdateAsync(deduction, cancellationToken),
            deduction,
            cancellationToken);

        return new DeductionDto(
            deduction.Id,
            deduction.EmployeeId,
            deduction.DeductionType,
            deduction.Description,
            deduction.Amount,
            deduction.IsPercentage,
            deduction.IsActive,
            deduction.CreatedAt,
            deduction.UpdatedAt);
    }
}
