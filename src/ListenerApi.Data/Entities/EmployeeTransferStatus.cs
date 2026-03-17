namespace ListenerApi.Data.Entities;

public class EmployeeTransferStatus
{
    public Guid EmployeeId { get; set; }
    public bool CanTransfer { get; set; } = true;
    public bool PeriodCountLimitReached { get; set; }
    public bool PeriodAmountLimitReached { get; set; }
    public bool DailyLimitReached { get; set; }
    public int TransferCount { get; set; }
    public decimal TotalAmountTransferred { get; set; }
    public int DailyTransferCount { get; set; }
    public int PeriodTransferLimit { get; set; } = 5;
    public decimal PeriodAmountLimit { get; set; } = 10000m;
    public int DailyTransferLimit { get; set; } = 1;
    public long PayPeriodNumber { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public EmployeeRecord Employee { get; set; } = null!;
}
