namespace TransferService.Domain.Entities;

public class EmployeeTransferLimits
{
    public Guid EmployeeId { get; set; }
    public int MaxTransfersPerPayPeriod { get; set; }
    public decimal MaxAmountPerPayPeriod { get; set; }
    public int MaxTransfersPerDay { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public static EmployeeTransferLimits Create(
        Guid employeeId,
        int maxPerPayPeriod,
        decimal maxAmountPerPayPeriod,
        int maxPerDay)
    {
        return new EmployeeTransferLimits
        {
            EmployeeId = employeeId,
            MaxTransfersPerPayPeriod = maxPerPayPeriod,
            MaxAmountPerPayPeriod = maxAmountPerPayPeriod,
            MaxTransfersPerDay = maxPerDay
        };
    }

    public void Update(int maxPerPayPeriod, decimal maxAmountPerPayPeriod, int maxPerDay)
    {
        MaxTransfersPerPayPeriod = maxPerPayPeriod;
        MaxAmountPerPayPeriod = maxAmountPerPayPeriod;
        MaxTransfersPerDay = maxPerDay;
        UpdatedAt = DateTime.UtcNow;
    }
}
