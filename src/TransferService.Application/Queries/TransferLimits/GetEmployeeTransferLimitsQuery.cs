using MediatR;
using TransferService.Application.DTOs;
using TransferService.Domain.Repositories;

namespace TransferService.Application.Queries.TransferLimits;

public record GetEmployeeTransferLimitsQuery(Guid EmployeeId) : IRequest<EmployeeTransferLimitsDto?>;

public class GetEmployeeTransferLimitsQueryHandler : IRequestHandler<GetEmployeeTransferLimitsQuery, EmployeeTransferLimitsDto?>
{
    private readonly IEmployeeTransferLimitsRepository _repository;

    public GetEmployeeTransferLimitsQueryHandler(IEmployeeTransferLimitsRepository repository)
    {
        _repository = repository;
    }

    public async Task<EmployeeTransferLimitsDto?> Handle(GetEmployeeTransferLimitsQuery request, CancellationToken cancellationToken)
    {
        var limits = await _repository.GetByEmployeeIdAsync(request.EmployeeId, cancellationToken);
        if (limits == null) return null;

        return new EmployeeTransferLimitsDto(
            limits.EmployeeId,
            limits.MaxTransfersPerPayPeriod,
            limits.MaxAmountPerPayPeriod,
            limits.MaxTransfersPerDay);
    }
}
