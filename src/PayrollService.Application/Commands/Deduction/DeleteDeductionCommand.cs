using MediatR;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Commands.Deduction;

public record DeleteDeductionCommand(Guid Id) : IRequest<bool>;

public class DeleteDeductionCommandHandler : IRequestHandler<DeleteDeductionCommand, bool>
{
    private readonly IDeductionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteDeductionCommandHandler(IDeductionRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> Handle(DeleteDeductionCommand request, CancellationToken cancellationToken)
    {
        var deduction = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Deduction with ID {request.Id} not found.");

        deduction.Deactivate();

        await _unitOfWork.ExecuteAsync(
            async () => await _repository.UpdateAsync(deduction, cancellationToken),
            deduction,
            cancellationToken);

        return true;
    }
}
