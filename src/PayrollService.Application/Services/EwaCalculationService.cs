using PayrollService.Domain.Entities;
using PayrollService.Domain.Enums;

namespace PayrollService.Application.Services;

public class EwaCalculationService
{
    private static readonly Dictionary<string, decimal> FederalTaxRates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Single"] = 0.22m,
        ["Married"] = 0.12m,
        ["HeadOfHousehold"] = 0.15m
    };

    private static readonly Dictionary<string, decimal> StateTaxRates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CA"] = 0.06m,
        ["NY"] = 0.055m,
        ["TX"] = 0.00m,
        ["FL"] = 0.00m,
        ["WA"] = 0.00m
    };

    private const decimal SocialSecurityRate = 0.062m;
    private const decimal MedicareRate = 0.0145m;
    private const decimal SocialSecurityWageBase = 168_600m;
    private const decimal DefaultStateTaxRate = 0.04m;

    public decimal CalculateGrossBalance(PayType payType, decimal payRate, decimal hoursWorked)
    {
        return payType switch
        {
            PayType.Hourly => Math.Round(hoursWorked * payRate, 2),
            PayType.Salary => Math.Round(payRate / 52m / 40m * hoursWorked, 2),
            _ => 0m
        };
    }

    public decimal CalculateHoursForSalaried(int daysWorkedInPeriod, int totalWorkDaysInPeriod)
    {
        if (totalWorkDaysInPeriod <= 0)
            return 0m;

        return Math.Min(daysWorkedInPeriod, totalWorkDaysInPeriod) * 8m;
    }

    public TaxEstimateResult EstimateTaxWithholdings(decimal gross, TaxInformation taxInfo, int payPeriodsPerYear)
    {
        if (gross <= 0)
            return new TaxEstimateResult(0m, 0m, 0m, 0m, 0m, 0m);

        var annualizedGross = gross * payPeriodsPerYear;

        // Social Security: 6.2% up to wage base
        var socialSecurity = annualizedGross <= SocialSecurityWageBase
            ? Math.Round(gross * SocialSecurityRate, 2)
            : 0m;

        // Medicare: 1.45%
        var medicare = Math.Round(gross * MedicareRate, 2);

        // Federal income tax: simplified flat rate by filing status
        var federalRate = FederalTaxRates.GetValueOrDefault(taxInfo.FederalFilingStatus, 0.22m);
        var federalIncomeTax = Math.Round(gross * federalRate, 2);

        // State income tax: simplified flat rate by state
        var stateRate = StateTaxRates.GetValueOrDefault(taxInfo.State, DefaultStateTaxRate);
        var stateIncomeTax = Math.Round(gross * stateRate, 2);

        // Additional withholdings prorated per period
        var additionalFederal = payPeriodsPerYear > 0
            ? Math.Round(taxInfo.AdditionalFederalWithholding / payPeriodsPerYear, 2)
            : 0m;
        var additionalState = payPeriodsPerYear > 0
            ? Math.Round(taxInfo.AdditionalStateWithholding / payPeriodsPerYear, 2)
            : 0m;

        return new TaxEstimateResult(
            socialSecurity,
            medicare,
            federalIncomeTax,
            stateIncomeTax,
            additionalFederal,
            additionalState);
    }

    public decimal CalculateNetBalance(decimal gross, IEnumerable<Deduction> deductions, TaxEstimateResult taxEstimate)
    {
        var totalTax = taxEstimate.Total;

        var totalDeductions = 0m;
        foreach (var deduction in deductions)
        {
            if (!deduction.IsActive)
                continue;

            totalDeductions += deduction.IsPercentage
                ? Math.Round(gross * (deduction.Amount / 100m), 2)
                : deduction.Amount;
        }

        return Math.Round(Math.Max(gross - totalTax - totalDeductions, 0m), 2);
    }

    public EwaFinalResult CalculateFinalBalance(
        decimal net,
        decimal accessPercentage,
        decimal dailyTransferLimit,
        decimal outstandingAdvances,
        decimal minimumThreshold)
    {
        var accessible = Math.Round(net * accessPercentage, 2);
        var afterAdvances = Math.Max(accessible - outstandingAdvances, 0m);
        var capped = Math.Min(afterAdvances, dailyTransferLimit);
        var finalBalance = Math.Floor(capped); // Floor to nearest dollar per PDF spec

        var isEligible = finalBalance >= minimumThreshold;

        return new EwaFinalResult(
            finalBalance,
            isEligible,
            dailyTransferLimit,
            dailyTransferLimit); // remainingDailyLimit = full limit (no prior transfers tracked)
    }
}

public record TaxEstimateResult(
    decimal SocialSecurity,
    decimal Medicare,
    decimal FederalIncomeTax,
    decimal StateIncomeTax,
    decimal AdditionalFederalWithholding,
    decimal AdditionalStateWithholding)
{
    public decimal Total => SocialSecurity + Medicare + FederalIncomeTax + StateIncomeTax
                            + AdditionalFederalWithholding + AdditionalStateWithholding;
}

public record EwaFinalResult(
    decimal FinalBalance,
    bool IsTransferEligible,
    decimal DailyTransferLimit,
    decimal RemainingDailyLimit);
