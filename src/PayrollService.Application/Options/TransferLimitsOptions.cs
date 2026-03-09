namespace PayrollService.Application.Options;

public class TransferLimitsOptions
{
    public const string SectionName = "TransferLimits";

    public int MaxPerPayPeriod { get; set; } = 5;
    public decimal MaxAmountPerPayPeriod { get; set; } = 10000m;
    public int MaxPerDay { get; set; } = 1;
}
