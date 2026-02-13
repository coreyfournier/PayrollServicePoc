using MediatR;
using Microsoft.Extensions.Options;
using PayrollService.Application.Configuration;
using PayrollService.Application.DTOs;
using PayrollService.Application.Services;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Queries.EarlyWageAccess;

public record GetEwaBalanceQuery(Guid EmployeeId, bool IncludeBreakdown = false) : IRequest<GetEwaBalanceResult>;

public record GetEwaBalanceResult(EwaBalanceDto? Balance, int StatusCode, string? ErrorCode, string? ErrorMessage);

public class GetEwaBalanceQueryHandler : IRequestHandler<GetEwaBalanceQuery, GetEwaBalanceResult>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly ITaxInformationRepository _taxInfoRepository;
    private readonly IDeductionRepository _deductionRepository;
    private readonly ITimeEntryRepository _timeEntryRepository;
    private readonly EwaCalculationService _calculationService;
    private readonly EwaSettings _settings;

    public GetEwaBalanceQueryHandler(
        IEmployeeRepository employeeRepository,
        ITaxInformationRepository taxInfoRepository,
        IDeductionRepository deductionRepository,
        ITimeEntryRepository timeEntryRepository,
        EwaCalculationService calculationService,
        IOptions<EwaSettings> settings)
    {
        _employeeRepository = employeeRepository;
        _taxInfoRepository = taxInfoRepository;
        _deductionRepository = deductionRepository;
        _timeEntryRepository = timeEntryRepository;
        _calculationService = calculationService;
        _settings = settings.Value;
    }

    public async Task<GetEwaBalanceResult> Handle(GetEwaBalanceQuery request, CancellationToken cancellationToken)
    {
        // 1. Fetch employee
        var employee = await _employeeRepository.GetByIdAsync(request.EmployeeId, cancellationToken);
        if (employee == null)
            return new GetEwaBalanceResult(null, 404, "EMPLOYEE_NOT_FOUND", "Employee not found.");

        if (!employee.IsActive)
            return new GetEwaBalanceResult(null, 422, "EMPLOYEE_INACTIVE", "Employee is inactive.");

        // 2. Fetch tax info
        var taxInfo = await _taxInfoRepository.GetByEmployeeIdAsync(request.EmployeeId, cancellationToken);
        if (taxInfo == null)
            return new GetEwaBalanceResult(null, 422, "INSUFFICIENT_DATA", "Tax information is missing for this employee.");

        // 3. Fetch active deductions
        var allDeductions = await _deductionRepository.GetByEmployeeIdAsync(request.EmployeeId, cancellationToken);
        var activeDeductions = allDeductions.Where(d => d.IsActive).ToList();

        // 4. Fetch time entries and filter to current pay period
        var allTimeEntries = await _timeEntryRepository.GetByEmployeeIdAsync(request.EmployeeId, cancellationToken);
        var (periodStart, periodEnd) = GetCurrentPayPeriod();
        var periodEntries = allTimeEntries
            .Where(te => te.ClockIn >= periodStart && te.ClockIn < periodEnd)
            .ToList();

        // 5. Calculate hours worked
        decimal hoursWorked;
        string calculationMethod;

        if (employee.PayType == PayType.Hourly)
        {
            hoursWorked = periodEntries
                .Where(te => te.ClockOut.HasValue)
                .Sum(te => te.HoursWorked);
            calculationMethod = "TIMESHEET";
        }
        else
        {
            var daysWorked = GetWorkDaysElapsedInPeriod(periodStart);
            hoursWorked = _calculationService.CalculateHoursForSalaried(daysWorked, _settings.WorkDaysPerPeriod);
            calculationMethod = "SALARY_PRORATION";
        }

        // 6. Calculate three-stage pipeline
        var gross = _calculationService.CalculateGrossBalance(employee.PayType, employee.PayRate, hoursWorked);

        var taxEstimate = _calculationService.EstimateTaxWithholdings(gross, taxInfo, _settings.PayPeriodsPerYear);

        var net = _calculationService.CalculateNetBalance(gross, activeDeductions, taxEstimate);

        var finalResult = _calculationService.CalculateFinalBalance(
            net,
            _settings.AccessPercentage,
            _settings.DailyTransferLimit,
            outstandingAdvances: 0m, // No advance tracking in this POC
            _settings.MinimumThreshold);

        // 7. Build deduction breakdown
        List<EwaDeductionItemDto>? deductionBreakdown = null;
        if (request.IncludeBreakdown)
        {
            deductionBreakdown = new List<EwaDeductionItemDto>();

            // Tax deductions
            if (taxEstimate.SocialSecurity > 0)
                deductionBreakdown.Add(new EwaDeductionItemDto("TAX", "Social Security (FICA)", taxEstimate.SocialSecurity));
            if (taxEstimate.Medicare > 0)
                deductionBreakdown.Add(new EwaDeductionItemDto("TAX", "Medicare", taxEstimate.Medicare));
            if (taxEstimate.FederalIncomeTax > 0)
                deductionBreakdown.Add(new EwaDeductionItemDto("TAX", "Federal Income Tax", taxEstimate.FederalIncomeTax));
            if (taxEstimate.StateIncomeTax > 0)
                deductionBreakdown.Add(new EwaDeductionItemDto("TAX", "State Income Tax", taxEstimate.StateIncomeTax));
            if (taxEstimate.AdditionalFederalWithholding > 0)
                deductionBreakdown.Add(new EwaDeductionItemDto("TAX", "Additional Federal Withholding", taxEstimate.AdditionalFederalWithholding));
            if (taxEstimate.AdditionalStateWithholding > 0)
                deductionBreakdown.Add(new EwaDeductionItemDto("TAX", "Additional State Withholding", taxEstimate.AdditionalStateWithholding));

            // Employee deductions
            foreach (var d in activeDeductions)
            {
                var amount = d.IsPercentage
                    ? Math.Round(gross * (d.Amount / 100m), 2)
                    : d.Amount;
                deductionBreakdown.Add(new EwaDeductionItemDto(d.DeductionType.ToString().ToUpperInvariant(), d.Description, amount));
            }
        }

        EwaBalanceUnavailableReasonDto? unavailableReason = null;
        if (!finalResult.IsTransferEligible)
        {
            unavailableReason = new EwaBalanceUnavailableReasonDto(
                "BELOW_MINIMUM",
                $"Balance of {finalResult.FinalBalance:C} is below the minimum threshold of {_settings.MinimumThreshold:C}.");
        }

        var dto = new EwaBalanceDto(
            EmployeeId: employee.Id,
            CalculationTimestamp: DateTime.UtcNow,
            GrossBalance: gross,
            NetBalance: net,
            FinalBalance: finalResult.FinalBalance,
            AccessPercentage: _settings.AccessPercentage,
            MinimumThreshold: _settings.MinimumThreshold,
            IsTransferEligible: finalResult.IsTransferEligible,
            Currency: "USD",
            DailyTransferLimit: finalResult.DailyTransferLimit,
            RemainingDailyLimit: finalResult.RemainingDailyLimit,
            OutstandingAdvances: 0m,
            CalculationMethod: calculationMethod,
            HoursWorked: hoursWorked,
            PayRate: employee.PayRate,
            Deductions: deductionBreakdown,
            BalanceUnavailableReason: unavailableReason);

        return new GetEwaBalanceResult(dto, 200, null, null);
    }

    private (DateTime Start, DateTime End) GetCurrentPayPeriod()
    {
        // Use a known reference Monday as the start of a pay period cycle
        var reference = new DateTime(2025, 1, 6, 0, 0, 0, DateTimeKind.Utc); // A Monday
        var now = DateTime.UtcNow;

        var periodLengthDays = _settings.WorkDaysPerPeriod + (_settings.WorkDaysPerPeriod / 5) * 2; // Add weekends
        // Bi-weekly = 14 calendar days
        if (periodLengthDays < 14)
            periodLengthDays = 14;

        var daysSinceReference = (now - reference).Days;
        var periodIndex = daysSinceReference / periodLengthDays;
        var periodStart = reference.AddDays(periodIndex * periodLengthDays);
        var periodEnd = periodStart.AddDays(periodLengthDays);

        return (periodStart, periodEnd);
    }

    private int GetWorkDaysElapsedInPeriod(DateTime periodStart)
    {
        var now = DateTime.UtcNow;
        var workDays = 0;
        var current = periodStart;

        while (current.Date <= now.Date && workDays < _settings.WorkDaysPerPeriod)
        {
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
                workDays++;
            current = current.AddDays(1);
        }

        return workDays;
    }
}
