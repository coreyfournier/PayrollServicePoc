namespace PayrollService.Domain.Enums;

public enum TransferStatus
{
    Initiated = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    AwaitingConfirmation = 5
}
