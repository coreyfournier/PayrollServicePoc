namespace ListenerApi.Data.Entities;

public class EmployeePayAttributes
{
    public Guid EmployeeId { get; set; }
    public long PayPeriodNumber { get; set; }
    public decimal GrossPay { get; set; }
    public decimal FederalTax { get; set; }
    public decimal StateTax { get; set; }
    public decimal AdditionalFederalWithholding { get; set; }
    public decimal AdditionalStateWithholding { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalFixedDeductions { get; set; }
    public decimal TotalPercentDeductions { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetPay { get; set; }
    public decimal PayRate { get; set; }
    public string PayType { get; set; } = string.Empty;
    public decimal TotalHoursWorked { get; set; }
    public string PayPeriodStart { get; set; } = string.Empty;
    public string PayPeriodEnd { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public EmployeeRecord Employee { get; set; } = null!;
}
