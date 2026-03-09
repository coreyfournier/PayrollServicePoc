namespace ListenerApi.Data.Entities;

public class TransferRecord
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public decimal Amount { get; set; }
    public long PayPeriodNumber { get; set; }
    public string Status { get; set; } = "Queued"; // Queued, Initiated, AwaitingConfirmation, Processing, Completed, Failed
    public decimal? CurrentBalance { get; set; }
    public DateTime InitiatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
    public string? ExternalReferenceId { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public EmployeeRecord Employee { get; set; } = null!;
}
