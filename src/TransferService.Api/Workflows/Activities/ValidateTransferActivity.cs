using Dapr.Workflow;
using Microsoft.Extensions.Options;
using TransferService.Application.Options;
using TransferService.Domain.Enums;
using TransferService.Domain.Repositories;
using TransferService.Domain.ValueObjects;

namespace TransferService.Api.Workflows.Activities;

public record ValidateTransferInput(Guid TransferId, Guid EmployeeId, decimal Amount, long PayPeriodNumber);
public record ValidateTransferResult(bool IsValid, string? Reason);

public class ValidateTransferActivity : WorkflowActivity<ValidateTransferInput, ValidateTransferResult>
{
    private readonly ITransferRepository _repository;
    private readonly TransferLimitsOptions _options;

    public ValidateTransferActivity(ITransferRepository repository, IOptions<TransferLimitsOptions> options)
    {
        _repository = repository;
        _options = options.Value;
    }

    public override async Task<ValidateTransferResult> RunAsync(WorkflowActivityContext context, ValidateTransferInput input)
    {
        var periodTransfers = await _repository.GetByEmployeeAndPayPeriodAsync(input.EmployeeId, input.PayPeriodNumber);
        var activeTransfers = periodTransfers
            .Where(t => t.Status != TransferStatus.Failed && t.Id != input.TransferId)
            .ToList();

        var currentCount = activeTransfers.Count;
        var currentAmount = activeTransfers.Sum(t => t.Amount);

        var todayStart = DateTime.UtcNow.Date;
        var transfersToday = await _repository.GetCountByEmployeeAndDateAsync(input.EmployeeId, todayStart);
        transfersToday = Math.Max(0, transfersToday - 1);

        var limits = new TransferLimits(_options.MaxPerPayPeriod, _options.MaxAmountPerPayPeriod, _options.MaxPerDay);
        var validation = limits.Validate(currentCount, currentAmount, input.Amount, transfersToday);

        return new ValidateTransferResult(validation.CanTransfer,
            validation.CanTransfer ? null : string.Join(" ", validation.Reasons));
    }
}
