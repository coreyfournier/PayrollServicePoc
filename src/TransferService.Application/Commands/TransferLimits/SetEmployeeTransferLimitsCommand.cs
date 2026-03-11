using MediatR;
using TransferService.Application.DTOs;
using TransferService.Domain.Entities;
using TransferService.Domain.Repositories;

namespace TransferService.Application.Commands.TransferLimits;

public record SetEmployeeTransferLimitsCommand(
    Guid EmployeeId,
    int MaxTransfersPerPayPeriod,
    decimal MaxAmountPerPayPeriod,
    int MaxTransfersPerDay) : IRequest<EmployeeTransferLimitsDto>;

public class SetEmployeeTransferLimitsCommandHandler : IRequestHandler<SetEmployeeTransferLimitsCommand, EmployeeTransferLimitsDto>
{
    private readonly IEmployeeTransferLimitsRepository _repository;

    public SetEmployeeTransferLimitsCommandHandler(IEmployeeTransferLimitsRepository repository)
    {
        _repository = repository;
    }

    public async Task<EmployeeTransferLimitsDto> Handle(SetEmployeeTransferLimitsCommand request, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByEmployeeIdAsync(request.EmployeeId, cancellationToken);

        if (existing != null)
        {
            existing.Update(request.MaxTransfersPerPayPeriod, request.MaxAmountPerPayPeriod, request.MaxTransfersPerDay);
            await _repository.UpsertAsync(existing, cancellationToken);
        }
        else
        {
            existing = EmployeeTransferLimits.Create(
                request.EmployeeId,
                request.MaxTransfersPerPayPeriod,
                request.MaxAmountPerPayPeriod,
                request.MaxTransfersPerDay);
            await _repository.UpsertAsync(existing, cancellationToken);
        }

        return new EmployeeTransferLimitsDto(
            existing.EmployeeId,
            existing.MaxTransfersPerPayPeriod,
            existing.MaxAmountPerPayPeriod,
            existing.MaxTransfersPerDay);
    }
}
