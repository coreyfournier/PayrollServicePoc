using MongoDB.Driver;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Enums;
using PayrollService.Infrastructure.Persistence;

namespace PayrollService.Infrastructure.Seeding;

public class DataSeeder
{
    private readonly MongoDbContext _context;

    public DataSeeder(MongoDbContext context)
    {
        _context = context;
    }

    public async Task SeedAsync()
    {
        var existingCount = await _context.Employees.CountDocumentsAsync(_ => true);
        if (existingCount > 0)
            return;

        var employees = CreateMockEmployees();
        await _context.Employees.InsertManyAsync(employees);

        // Create tax information for each employee
        var taxInfos = employees.Select(e => CreateTaxInfo(e.Id)).ToList();
        await _context.TaxInformation.InsertManyAsync(taxInfos);

        // Create deductions for some employees
        var deductions = new List<Deduction>();
        foreach (var employee in employees.Take(3))
        {
            deductions.AddRange(CreateDeductions(employee.Id));
        }
        await _context.Deductions.InsertManyAsync(deductions);
    }

    private static List<Employee> CreateMockEmployees()
    {
        return new List<Employee>
        {
            CreateEmployee("John", "Smith", "john.smith@company.com", PayType.Salary, 75000m, new DateTime(2020, 1, 15)),
            CreateEmployee("Sarah", "Johnson", "sarah.johnson@company.com", PayType.Hourly, 28.50m, new DateTime(2021, 3, 20)),
            CreateEmployee("Michael", "Williams", "michael.williams@company.com", PayType.Salary, 85000m, new DateTime(2019, 6, 1)),
            CreateEmployee("Emily", "Brown", "emily.brown@company.com", PayType.Hourly, 32.00m, new DateTime(2022, 9, 10)),
            CreateEmployee("David", "Davis", "david.davis@company.com", PayType.Salary, 95000m, new DateTime(2018, 11, 5))
        };
    }

    private static Employee CreateEmployee(string firstName, string lastName, string email, PayType payType, decimal payRate, DateTime hireDate)
    {
        return Employee.Create(firstName, lastName, email, payType, payRate, hireDate);
    }

    private static TaxInformation CreateTaxInfo(Guid employeeId)
    {
        var states = new[] { "CA", "NY", "TX", "FL", "WA" };
        var filingStatuses = new[] { "Single", "Married", "Head of Household" };
        var random = new Random();

        return TaxInformation.Create(
            employeeId,
            filingStatuses[random.Next(filingStatuses.Length)],
            random.Next(0, 4),
            0,
            states[random.Next(states.Length)],
            filingStatuses[random.Next(filingStatuses.Length)],
            random.Next(0, 4),
            0);
    }

    private static List<Deduction> CreateDeductions(Guid employeeId)
    {
        return new List<Deduction>
        {
            Deduction.Create(employeeId, DeductionType.Health, "Medical Insurance Premium", 250.00m, false),
            Deduction.Create(employeeId, DeductionType.Retirement401k, "401(k) Contribution", 6.0m, true)
        };
    }
}
