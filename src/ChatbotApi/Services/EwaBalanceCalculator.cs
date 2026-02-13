using System.Text.Json;

namespace ChatbotApi.Services;

public interface IEwaBalanceCalculator
{
    Task<string> GetEwaBalanceAsync(string employeeId);
}

public class EwaBalanceCalculator : IEwaBalanceCalculator
{
    private readonly IPayrollApiClient _payrollApiClient;
    private readonly ILogger<EwaBalanceCalculator> _logger;

    // EWA policy constants
    private const decimal MaxDailyWithdrawal = 200.00m;
    private const decimal MaxWithdrawalPercent = 0.70m; // 70% of net balance
    private const int MaxWithdrawalsPerDay = 1;

    // Simplified tax rates for POC (would come from a tax service in production)
    private const decimal FederalTaxRate = 0.22m;   // 22% federal bracket estimate
    private const decimal StateTaxRate = 0.05m;      // 5% state estimate
    private const decimal FicaRate = 0.0765m;        // 7.65% (Social Security 6.2% + Medicare 1.45%)

    public EwaBalanceCalculator(IPayrollApiClient payrollApiClient, ILogger<EwaBalanceCalculator> logger)
    {
        _payrollApiClient = payrollApiClient;
        _logger = logger;
    }

    public async Task<string> GetEwaBalanceAsync(string employeeId)
    {
        try
        {
            // Fetch all required data in parallel
            var employeeTask = _payrollApiClient.GetEmployeeByIdAsync(employeeId);
            var timeEntriesTask = _payrollApiClient.GetTimeEntriesAsync(employeeId);
            var deductionsTask = _payrollApiClient.GetDeductionsAsync(employeeId);
            var taxInfoTask = _payrollApiClient.GetTaxInformationAsync(employeeId);

            await Task.WhenAll(employeeTask, timeEntriesTask, deductionsTask, taxInfoTask);

            var employeeJson = await employeeTask;
            var timeEntriesJson = await timeEntriesTask;
            var deductionsJson = await deductionsTask;
            var taxInfoJson = await taxInfoTask;

            // Parse employee
            using var employeeDoc = JsonDocument.Parse(employeeJson);
            var employee = employeeDoc.RootElement;

            if (employee.TryGetProperty("error", out _))
                return employeeJson; // Pass through API errors

            var payType = employee.GetProperty("payType").GetInt32();
            var payRate = employee.GetProperty("payRate").GetDecimal();
            var firstName = employee.GetProperty("firstName").GetString() ?? "Employee";
            var lastName = employee.GetProperty("lastName").GetString() ?? "";

            // Calculate gross earned wages for current pay period
            decimal grossEarned = CalculateGrossEarned(payType, payRate, timeEntriesJson);

            // Calculate deductions per pay period
            decimal totalDeductions = CalculateDeductions(deductionsJson, grossEarned);

            // Calculate taxes
            decimal estimatedTaxes = CalculateEstimatedTaxes(grossEarned, taxInfoJson);

            // Net earned = gross - taxes - deductions
            decimal netEarned = Math.Max(0, grossEarned - estimatedTaxes - totalDeductions);

            // Available for EWA withdrawal = min($200, 70% of net)
            decimal seventyPercentOfNet = netEarned * MaxWithdrawalPercent;
            decimal availableWithdrawal = Math.Min(MaxDailyWithdrawal, seventyPercentOfNet);
            availableWithdrawal = Math.Max(0, Math.Round(availableWithdrawal, 2));

            var result = new
            {
                employeeId,
                employeeName = $"{firstName} {lastName}",
                payPeriod = new
                {
                    start = GetPayPeriodStart().ToString("yyyy-MM-dd"),
                    end = GetPayPeriodEnd().ToString("yyyy-MM-dd")
                },
                grossEarnedWages = Math.Round(grossEarned, 2),
                estimatedTaxes = Math.Round(estimatedTaxes, 2),
                estimatedDeductions = Math.Round(totalDeductions, 2),
                netEarnedWages = Math.Round(netEarned, 2),
                ewaWithdrawal = new
                {
                    availableBalance = Math.Round(netEarned, 2),
                    maxDailyWithdrawal = MaxDailyWithdrawal,
                    withdrawalLimitPercent = $"{MaxWithdrawalPercent * 100}%",
                    availableToWithdrawToday = availableWithdrawal,
                    maxWithdrawalsPerDay = MaxWithdrawalsPerDay,
                    note = availableWithdrawal < MaxDailyWithdrawal
                        ? $"Withdrawal limited to 70% of net balance (${availableWithdrawal})"
                        : $"Withdrawal capped at daily maximum (${MaxDailyWithdrawal})"
                }
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating EWA balance for employee {EmployeeId}", employeeId);
            return $"{{\"error\": \"Failed to calculate EWA balance: {ex.Message}\"}}";
        }
    }

    private decimal CalculateGrossEarned(int payType, decimal payRate, string timeEntriesJson)
    {
        if (payType == 2) // Salary
        {
            // Prorate salary: annual / 26 biweekly periods, then by days elapsed in period
            decimal biweeklyPay = payRate / 26m;
            var periodStart = GetPayPeriodStart();
            var today = DateTime.UtcNow.Date;
            var periodEnd = GetPayPeriodEnd();

            int totalDaysInPeriod = Math.Max(1, (int)(periodEnd - periodStart).TotalDays);
            int daysElapsed = Math.Max(1, (int)(today - periodStart).TotalDays + 1);
            daysElapsed = Math.Min(daysElapsed, totalDaysInPeriod);

            return biweeklyPay * ((decimal)daysElapsed / totalDaysInPeriod);
        }
        else // Hourly
        {
            // Sum hours worked from time entries in current pay period
            decimal totalHours = 0;
            var periodStart = GetPayPeriodStart();

            try
            {
                using var doc = JsonDocument.Parse(timeEntriesJson);
                var entries = doc.RootElement;

                if (entries.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in entries.EnumerateArray())
                    {
                        if (entry.TryGetProperty("hoursWorked", out var hoursEl))
                        {
                            var hours = hoursEl.GetDecimal();

                            // Only count entries in current pay period
                            if (entry.TryGetProperty("clockIn", out var clockInEl))
                            {
                                if (DateTime.TryParse(clockInEl.GetString(), out var clockIn))
                                {
                                    if (clockIn.Date >= periodStart)
                                        totalHours += hours;
                                }
                            }
                            else
                            {
                                totalHours += hours; // Include if no date filter possible
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not parse time entries, defaulting to 0 hours");
            }

            return totalHours * payRate;
        }
    }

    private decimal CalculateDeductions(string deductionsJson, decimal grossEarned)
    {
        decimal total = 0;

        try
        {
            using var doc = JsonDocument.Parse(deductionsJson);
            var deductions = doc.RootElement;

            if (deductions.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in deductions.EnumerateArray())
                {
                    bool isActive = true;
                    if (d.TryGetProperty("isActive", out var activeEl))
                        isActive = activeEl.GetBoolean();

                    if (!isActive) continue;

                    var amount = d.TryGetProperty("amount", out var amtEl) ? amtEl.GetDecimal() : 0;
                    var isPercentage = d.TryGetProperty("isPercentage", out var pctEl) && pctEl.GetBoolean();

                    if (isPercentage)
                        total += grossEarned * (amount / 100m);
                    else
                        total += amount;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse deductions, defaulting to 0");
        }

        return total;
    }

    private decimal CalculateEstimatedTaxes(decimal grossEarned, string taxInfoJson)
    {
        decimal federalRate = FederalTaxRate;
        decimal stateRate = StateTaxRate;
        decimal additionalFederal = 0;
        decimal additionalState = 0;

        try
        {
            using var doc = JsonDocument.Parse(taxInfoJson);
            var taxInfo = doc.RootElement;

            // Adjust for additional withholding if specified
            if (taxInfo.TryGetProperty("additionalFederalWithholding", out var addFedEl))
                additionalFederal = addFedEl.GetDecimal();

            if (taxInfo.TryGetProperty("additionalStateWithholding", out var addStateEl))
                additionalState = addStateEl.GetDecimal();

            // Adjust rates by allowances (simplified: each allowance reduces effective rate slightly)
            if (taxInfo.TryGetProperty("federalAllowances", out var fedAllowEl))
            {
                int allowances = fedAllowEl.GetInt32();
                federalRate = Math.Max(0.10m, federalRate - (allowances * 0.02m));
            }

            if (taxInfo.TryGetProperty("stateAllowances", out var stateAllowEl))
            {
                int allowances = stateAllowEl.GetInt32();
                stateRate = Math.Max(0, stateRate - (allowances * 0.01m));
            }

            // Check for no-income-tax states
            if (taxInfo.TryGetProperty("state", out var stateEl))
            {
                var state = stateEl.GetString()?.ToUpperInvariant();
                if (state is "FL" or "TX" or "WA" or "NV" or "WY" or "SD" or "AK" or "NH" or "TN")
                    stateRate = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse tax info, using default rates");
        }

        decimal federalTax = grossEarned * federalRate + additionalFederal;
        decimal stateTax = grossEarned * stateRate + additionalState;
        decimal ficaTax = grossEarned * FicaRate;

        return federalTax + stateTax + ficaTax;
    }

    // Simplified pay period: biweekly, starting from a known Monday
    private static DateTime GetPayPeriodStart()
    {
        var today = DateTime.UtcNow.Date;
        // Anchor to a known pay period start (Monday Jan 1, 2024)
        var anchor = new DateTime(2024, 1, 1);
        int daysSinceAnchor = (int)(today - anchor).TotalDays;
        int daysIntoPeriod = daysSinceAnchor % 14;
        return today.AddDays(-daysIntoPeriod);
    }

    private static DateTime GetPayPeriodEnd()
    {
        return GetPayPeriodStart().AddDays(13);
    }
}
