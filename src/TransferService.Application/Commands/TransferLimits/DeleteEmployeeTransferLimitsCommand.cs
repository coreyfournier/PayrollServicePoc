using MediatR;
using TransferService.Domain.Repositories;

namespace TransferService.Application.Commands.TransferLimits;

public record DeleteEmployeeTransferLimitsCommand(Guid EmployeeId) : IRequest;

public class DeleteEmployeeTransferLimitsCommandHandler : IRequestHandler<DeleteEmployeeTransferLimitsCommand>
{
    private readonly IEmployeeTransferLimitsRepository _repository;

    public DeleteEmployeeTransferLimitsCommandHandler(IEmployeeTransferLimitsRepository repository)
    {
        _repository = repository;
    }

    public async Task Handle(DeleteEmployeeTransferLimitsCommand request, CancellationToken cancellationToken)
    {
        await _repository.DeleteAsync(request.EmployeeId, cancellationToken);
    }
}
