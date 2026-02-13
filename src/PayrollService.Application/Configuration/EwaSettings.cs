namespace PayrollService.Application.Configuration;

public class EwaSettings
{
    public const string SectionName = "EarlyWageAccess";

    public decimal AccessPercentage { get; set; } = 0.50m;
    public decimal DailyTransferLimit { get; set; } = 500.00m;
    public decimal MinimumThreshold { get; set; } = 20.00m;
    public int PayPeriodsPerYear { get; set; } = 26;
    public int WorkDaysPerPeriod { get; set; } = 10;
}
