using PayrollService.Domain.Entities;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Orleans.Grains;

namespace PayrollService.Infrastructure.Seeding;

public class DataSeeder
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly ITaxInformationRepository _taxRepository;
    private readonly IDeductionRepository _deductionRepository;
    private readonly IGrainFactory _grainFactory;

    public DataSeeder(
        IEmployeeRepository employeeRepository,
        ITaxInformationRepository taxRepository,
        IDeductionRepository deductionRepository,
        IGrainFactory grainFactory)
    {
        _employeeRepository = employeeRepository;
        _taxRepository = taxRepository;
        _deductionRepository = deductionRepository;
        _grainFactory = grainFactory;
    }

    public async Task SeedAsync()
    {
        var existingEmployees = await _employeeRepository.GetAllAsync();
        if (existingEmployees.Any())
            return;

        var employees = await SeedEmployeesAsync();

        foreach (var employee in employees)
        {
            await SeedTaxInfoAsync(employee.Id);
        }

        foreach (var employee in employees.Take(3))
        {
            await SeedDeductionsAsync(employee.Id);
        }
    }

    private async Task<List<Employee>> SeedEmployeesAsync()
    {
        var employeeData = new[]
        {
            ("John", "Smith", "john.smith@company.com", PayType.Salary, 75000m, new DateTime(2020, 1, 15)),
            ("Sarah", "Johnson", "sarah.johnson@company.com", PayType.Hourly, 28.50m, new DateTime(2021, 3, 20)),
            ("Michael", "Williams", "michael.williams@company.com", PayType.Salary, 85000m, new DateTime(2019, 6, 1)),
            ("Emily", "Brown", "emily.brown@company.com", PayType.Hourly, 32.00m, new DateTime(2022, 9, 10)),
            ("David", "Davis", "david.davis@company.com", PayType.Salary, 95000m, new DateTime(2018, 11, 5))
        };

        var employees = new List<Employee>();
        foreach (var (firstName, lastName, email, payType, payRate, hireDate) in employeeData)
        {
            var employee = Employee.Create(firstName, lastName, email, payType, payRate, hireDate);
            var created = await _employeeRepository.AddAsync(employee);
            employees.Add(created);
        }

        return employees;
    }

    private async Task SeedTaxInfoAsync(Guid employeeId)
    {
        var states = new[] { "CA", "NY", "TX", "FL", "WA" };
        var filingStatuses = new[] { "Single", "Married", "Head of Household" };
        var random = new Random();

        var taxInfo = TaxInformation.Create(
            employeeId,
            filingStatuses[random.Next(filingStatuses.Length)],
            random.Next(0, 4),
            0,
            states[random.Next(states.Length)],
            filingStatuses[random.Next(filingStatuses.Length)],
            random.Next(0, 4),
            0);

        await _taxRepository.AddAsync(taxInfo);
    }

    private async Task SeedDeductionsAsync(Guid employeeId)
    {
        var deductions = new[]
        {
            Deduction.Create(employeeId, DeductionType.Health, "Medical Insurance Premium", 250.00m, false),
            Deduction.Create(employeeId, DeductionType.Retirement401k, "401(k) Contribution", 6.0m, true)
        };

        foreach (var deduction in deductions)
        {
            await _deductionRepository.AddAsync(deduction);
        }
    }
}
