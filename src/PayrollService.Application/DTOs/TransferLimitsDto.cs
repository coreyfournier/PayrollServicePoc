namespace PayrollService.Application.DTOs;

public record TransferLimitsDto(
    int MaxTransfersPerPayPeriod,
    decimal MaxAmountPerPayPeriod,
    int MaxTransfersPerDay,
    int CurrentPeriodCount,
    decimal CurrentPeriodAmount,
    int TransfersToday,
    bool CanTransfer,
    List<string> Reasons);
