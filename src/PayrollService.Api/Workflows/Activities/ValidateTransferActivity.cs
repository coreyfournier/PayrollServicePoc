using Dapr.Workflow;
using Microsoft.Extensions.Options;
using PayrollService.Application.Options;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Repositories;
using PayrollService.Domain.ValueObjects;

namespace PayrollService.Api.Workflows.Activities;

public record ValidateTransferInput(Guid TransferId, Guid EmployeeId, decimal Amount, long PayPeriodNumber);
public record ValidateTransferResult(bool IsValid, string? Reason);

public class ValidateTransferActivity : WorkflowActivity<ValidateTransferInput, ValidateTransferResult>
{
    private readonly ITransferRepository _transferRepository;
    private readonly TransferLimitsOptions _options;

    public ValidateTransferActivity(ITransferRepository transferRepository, IOptions<TransferLimitsOptions> options)
    {
        _transferRepository = transferRepository;
        _options = options.Value;
    }

    public override async Task<ValidateTransferResult> RunAsync(WorkflowActivityContext context, ValidateTransferInput input)
    {
        var periodTransfers = await _transferRepository.GetByEmployeeAndPayPeriodAsync(input.EmployeeId, input.PayPeriodNumber);
        // Exclude the current transfer (already counted by the actor that created it)
        var activeTransfers = periodTransfers
            .Where(t => t.Status != TransferStatus.Failed && t.Id != input.TransferId)
            .ToList();

        var currentCount = activeTransfers.Count;
        var currentAmount = activeTransfers.Sum(t => t.Amount);

        var todayStart = DateTime.UtcNow.Date;
        var transfersToday = await _transferRepository.GetCountByEmployeeAndDateAsync(input.EmployeeId, todayStart);
        // Exclude self from daily count too
        transfersToday = Math.Max(0, transfersToday - 1);

        var limits = new TransferLimits(_options.MaxPerPayPeriod, _options.MaxAmountPerPayPeriod, _options.MaxPerDay);
        var validation = limits.Validate(currentCount, currentAmount, input.Amount, transfersToday);

        if (!validation.CanTransfer)
            return new ValidateTransferResult(false, string.Join(" ", validation.Reasons));

        return new ValidateTransferResult(true, null);
    }
}
