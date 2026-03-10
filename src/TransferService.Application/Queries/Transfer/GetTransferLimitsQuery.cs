using MediatR;
using Microsoft.Extensions.Options;
using TransferService.Application.DTOs;
using TransferService.Application.Options;
using TransferService.Domain.Enums;
using TransferService.Domain.Repositories;
using TransferService.Domain.ValueObjects;

namespace TransferService.Application.Queries.Transfer;

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
        var activeTransfers = periodTransfers.Where(t => t.Status != TransferStatus.Failed).ToList();

        var currentCount = activeTransfers.Count;
        var currentAmount = activeTransfers.Sum(t => t.Amount);

        var todayStart = DateTime.UtcNow.Date;
        var transfersToday = await _repository.GetCountByEmployeeAndDateAsync(
            request.EmployeeId, todayStart, cancellationToken);

        var limits = new TransferLimits(_options.MaxPerPayPeriod, _options.MaxAmountPerPayPeriod, _options.MaxPerDay);
        var validation = limits.Validate(currentCount, currentAmount, 0, transfersToday);

        return new TransferLimitsDto(
            limits.MaxTransfersPerPayPeriod,
            limits.MaxAmountPerPayPeriod,
            limits.MaxTransfersPerDay,
            currentCount,
            currentAmount,
            transfersToday,
            validation.CanTransfer);
    }
}
