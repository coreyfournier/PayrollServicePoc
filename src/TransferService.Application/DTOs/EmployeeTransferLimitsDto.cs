namespace TransferService.Application.DTOs;

public record EmployeeTransferLimitsDto(
    Guid EmployeeId,
    int MaxTransfersPerPayPeriod,
    decimal MaxAmountPerPayPeriod,
    int MaxTransfersPerDay);
