namespace ListenerApi.Data.Entities;

public class BankAccount
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string AccountNumberMasked { get; set; } = string.Empty;
    public string RoutingNumber { get; set; } = string.Empty;
    public int AccountType { get; set; } // 1=Checking, 2=Savings
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public EmployeeRecord Employee { get; set; } = null!;
}
