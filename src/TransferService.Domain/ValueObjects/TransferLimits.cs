using TransferService.Domain.Entities;

namespace TransferService.Domain.ValueObjects;

public record TransferLimits(
    int MaxTransfersPerPayPeriod,
    decimal MaxAmountPerPayPeriod,
    int MaxTransfersPerDay)
{
    public static TransferLimits Default => new(5, 10000m, 1);

    public static TransferLimits FromEmployeeOverride(EmployeeTransferLimits overrides) =>
        new(overrides.MaxTransfersPerPayPeriod, overrides.MaxAmountPerPayPeriod, overrides.MaxTransfersPerDay);

    public TransferLimitValidationResult Validate(
        int currentPeriodCount,
        decimal currentPeriodAmount,
        decimal requestedAmount,
        int transfersToday)
    {
        var reasons = new List<string>();

        if (transfersToday >= MaxTransfersPerDay)
            reasons.Add($"Daily transfer limit reached ({MaxTransfersPerDay} per day).");

        if (currentPeriodCount >= MaxTransfersPerPayPeriod)
            reasons.Add($"Pay period transfer limit reached ({MaxTransfersPerPayPeriod} per period).");

        if (currentPeriodAmount + requestedAmount > MaxAmountPerPayPeriod)
            reasons.Add($"Pay period amount limit would be exceeded (${MaxAmountPerPayPeriod} max, ${currentPeriodAmount} already transferred).");

        return new TransferLimitValidationResult(reasons.Count == 0, reasons);
    }
}

public record TransferLimitValidationResult(bool CanTransfer, List<string> Reasons);
