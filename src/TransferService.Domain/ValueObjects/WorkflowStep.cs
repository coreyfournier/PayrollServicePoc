namespace TransferService.Domain.ValueObjects;

public class WorkflowStep
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Detail { get; set; }
    public int RetryCount { get; set; }

    public static class Names
    {
        public const string Validation = "Validation";
        public const string BalanceCheck = "BalanceCheck";
        public const string AwaitingConfirmation = "AwaitingConfirmation";
        public const string BankTransfer = "BankTransfer";
        public const string Complete = "Complete";
    }

    public static class Statuses
    {
        public const string Pending = "Pending";
        public const string InProgress = "InProgress";
        public const string Completed = "Completed";
        public const string Failed = "Failed";
    }
}
