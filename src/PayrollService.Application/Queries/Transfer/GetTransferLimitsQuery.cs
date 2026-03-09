using MediatR;
using Microsoft.Extensions.Options;
using PayrollService.Application.DTOs;
using PayrollService.Application.Options;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Repositories;
using PayrollService.Domain.ValueObjects;

namespace PayrollService.Application.Queries.Transfer;

public record GetTransferLimitsQuery(Guid EmployeeId, long PayPeriodNumber) : IRequest<TransferLimitsDto>;

public class GetTransferLimitsQueryHandler : IRequestHandler<GetTransferLimitsQuery, TransferLimitsDto>
{
    private readonly ITransferRepository _repository;
    private readonly TransferLimitsOptions _options;

    public GetTransferLimitsQueryHandler(ITransferRepository repository, IOptions<TransferLimitsOptions> options)
    {
        _repository = repository;
        _options = options.Value;
    }

    public async Task<TransferLimitsDto> Handle(GetTransferLimitsQuery request, CancellationToken cancellationToken)
    {
        var periodTransfers = await _repository.GetByEmployeeAndPayPeriodAsync(
            request.EmployeeId, request.PayPeriodNumber, cancellationToken);

        var activeTransfers = periodTransfers
            .Where(t => t.Status != TransferStatus.Failed)
            .ToList();

        var currentPeriodCount = activeTransfers.Count;
        var currentPeriodAmount = activeTransfers.Sum(t => t.Amount);

        var todayStart = DateTime.UtcNow.Date;
        var transfersToday = await _repository.GetCountByEmployeeAndDateAsync(
            request.EmployeeId, todayStart, cancellationToken);

        var limits = new TransferLimits(
            _options.MaxPerPayPeriod,
            _options.MaxAmountPerPayPeriod,
            _options.MaxPerDay);

        var validation = limits.Validate(currentPeriodCount, currentPeriodAmount, 0, transfersToday);

        return new TransferLimitsDto(
            limits.MaxTransfersPerPayPeriod,
            limits.MaxAmountPerPayPeriod,
            limits.MaxTransfersPerDay,
            currentPeriodCount,
            currentPeriodAmount,
            transfersToday,
            validation.CanTransfer,
            validation.Reasons);
    }
}
