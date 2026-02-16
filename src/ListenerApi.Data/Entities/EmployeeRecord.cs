namespace ListenerApi.Data.Entities;

public class EmployeeRecord
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PayType { get; set; } = string.Empty;
    public decimal? PayRate { get; set; }
    public decimal PayPeriodHours { get; set; } = 40;
    public bool IsActive { get; set; }

    // Idempotency tracking
    public string LastEventType { get; set; } = string.Empty;
    public DateTime LastEventTimestamp { get; set; }
    public Guid LastEventId { get; set; }

    // Audit
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
