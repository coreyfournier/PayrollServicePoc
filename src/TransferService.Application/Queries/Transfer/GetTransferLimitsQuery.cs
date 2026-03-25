using MediatR;
using Microsoft.Extensions.Options;
using TransferService.Application.DTOs;
using TransferService.Application.Options;
using TransferService.Domain.Enums;
using TransferService.Domain.Repositories;
using DomainTransferLimits = TransferService.Domain.ValueObjects.TransferLimits;

namespace TransferService.Application.Queries.Transfer;

public record GetTransferLimitsQuery(Guid EmployeeId, long PayPeriodNumber) : IRequest<TransferLimitsDto>;

public class GetTransferLimitsQueryHandler : IRequestHandler<GetTransferLimitsQuery, TransferLimitsDto>
{
    private readonly ITransferRepository _repository;
    private readonly IEmployeeTransferLimitsRepository _limitsRepository;
    private readonly TransferLimitsOptions _options;

    public GetTransferLimitsQueryHandler(
        ITransferRepository repository,
        IEmployeeTransferLimitsRepository limitsRepository,
        IOptions<TransferLimitsOptions> options)
    {
        _repository = repository;
        _limitsRepository = limitsRepository;
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
            request.EmployeeId, todayStart, cancellationToken: cancellationToken);

        var employeeOverride = await _limitsRepository.GetByEmployeeIdAsync(request.EmployeeId, cancellationToken);
        var isCustom = employeeOverride != null;
        var limits = employeeOverride != null
            ? DomainTransferLimits.FromEmployeeOverride(employeeOverride)
            : new DomainTransferLimits(_options.MaxPerPayPeriod, _options.MaxAmountPerPayPeriod, _options.MaxPerDay);
        var validation = limits.Validate(currentCount, currentAmount, 0, transfersToday);

        return new TransferLimitsDto(
            limits.MaxTransfersPerPayPeriod,
            limits.MaxAmountPerPayPeriod,
            limits.MaxTransfersPerDay,
            currentCount,
            currentAmount,
            transfersToday,
            validation.CanTransfer,
            isCustom);
    }
}
