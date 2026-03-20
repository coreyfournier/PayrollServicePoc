// tests/KafkaPipeline.Tests/NetPayProcessorTests.cs
using System.Text.Json;
using KafkaPipeline.Tests.Fixtures;
using KafkaPipeline.Tests.Helpers;

namespace KafkaPipeline.Tests;

[Collection("KafkaPipeline")]
public class NetPayProcessorTests
{
    private readonly KafkaFixture _fixture;

    public NetPayProcessorTests(KafkaFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SalaryEmployee_ProducesCorrectNetPay()
    {
        var employeeId = Guid.NewGuid().ToString();
        using var producer = new CloudEventProducer(_fixture.BootstrapServers);

        // Produce employee event (salary, $75,000/year, 40 hours/period)
        await producer.ProduceAsync("employee-events", employeeId, new
        {
            Id = employeeId,
            FirstName = "Test",
            LastName = "Salary",
            Email = "test.salary@example.com",
            PayType = 2, // Salary
            PayRate = 75000.0,
            PayPeriodHours = 40.0,
            IsActive = true,
            HireDate = "2024-01-15T00:00:00Z",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            DomainEvents = new[]
            {
                new { EventType = "employee.created", EventId = Guid.NewGuid().ToString(), OccurredOn = DateTime.UtcNow.ToString("O") }
            }
        });

        // Produce tax info (married, CA)
        await producer.ProduceAsync("employee-events", employeeId, new
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeId = employeeId,
            FederalFilingStatus = "Married",
            FederalAllowances = 2,
            AdditionalFederalWithholding = 0.0,
            State = "CA",
            StateFilingStatus = "Married",
            StateAllowances = 1,
            AdditionalStateWithholding = 0.0,
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            DomainEvents = new[]
            {
                new { EventType = "taxinfo.created", EventId = Guid.NewGuid().ToString(), OccurredOn = DateTime.UtcNow.ToString("O") }
            }
        });

        // Produce deduction (health $100 fixed)
        await producer.ProduceAsync("employee-events", employeeId, new
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeId = employeeId,
            DeductionType = 1, // Health
            Description = "Health Insurance",
            Amount = 100.0,
            IsPercentage = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            DomainEvents = new[]
            {
                new { EventType = "deduction.created", EventId = Guid.NewGuid().ToString(), OccurredOn = DateTime.UtcNow.ToString("O") }
            }
        });

        // Produce deduction (401k 5%)
        await producer.ProduceAsync("employee-events", employeeId, new
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeId = employeeId,
            DeductionType = 4, // Retirement401k
            Description = "401k",
            Amount = 5.0,
            IsPercentage = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            DomainEvents = new[]
            {
                new { EventType = "deduction.created", EventId = Guid.NewGuid().ToString(), OccurredOn = DateTime.UtcNow.ToString("O") }
            }
        });

        // Wait for the final message that includes both deductions.
        // Each event (employee, tax, deduction1, deduction2) triggers a recompute,
        // so we wait until we see a message with TOTAL_PERCENT_DEDUCTIONS > 0
        // (the 401k 5% deduction), meaning all events have been processed.
        using var consumer = new TopicConsumer(_fixture.BootstrapServers, $"test-netpay-{Guid.NewGuid():N}");
        consumer.Subscribe("employee-net-pay");

        var results = await consumer.ConsumeUntilAsync(
            messages => messages.Any(m =>
                m.Key.Contains(employeeId) &&
                m.Value.RootElement.GetProperty("TOTAL_PERCENT_DEDUCTIONS").GetDouble() > 0),
            TimeSpan.FromSeconds(60));

        var netPayMessage = results.Last(m =>
            m.Key.Contains(employeeId) &&
            m.Value.RootElement.GetProperty("TOTAL_PERCENT_DEDUCTIONS").GetDouble() > 0);
        var root = netPayMessage.Value.RootElement;

        // Gross pay = (75000 / 2080) * 40 = 1442.31 (annual / 2080 hours * payPeriodHours)
        var grossPay = root.GetProperty("GROSS_PAY").GetDouble();
        grossPay.Should().BeApproximately(1442.31, 0.1);

        // CA state tax ~= 9.3% of annualized
        var stateTax = root.GetProperty("STATE_TAX").GetDouble();
        stateTax.Should().BeGreaterThan(0);

        // Deductions: $100 fixed + 5% of gross
        var totalDeductions = root.GetProperty("TOTAL_DEDUCTIONS").GetDouble();
        totalDeductions.Should().BeApproximately(100.0 + (grossPay * 0.05), 1.0);

        // Net pay = gross - federal - state - deductions
        var netPay = root.GetProperty("NET_PAY").GetDouble();
        netPay.Should().BeGreaterThan(0);
        netPay.Should().BeLessThan(grossPay);
    }

    [Fact]
    public async Task HourlyEmployee_WithTimeEntries_ProducesCorrectGross()
    {
        var employeeId = Guid.NewGuid().ToString();
        using var producer = new CloudEventProducer(_fixture.BootstrapServers);

        // Produce hourly employee ($28.50/hr)
        await producer.ProduceAsync("employee-events", employeeId, new
        {
            Id = employeeId,
            FirstName = "Test",
            LastName = "Hourly",
            Email = "test.hourly@example.com",
            PayType = 1, // Hourly
            PayRate = 28.50,
            PayPeriodHours = 40.0,
            IsActive = true,
            HireDate = "2024-01-15T00:00:00Z",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            DomainEvents = new[]
            {
                new { EventType = "employee.created", EventId = Guid.NewGuid().ToString(), OccurredOn = DateTime.UtcNow.ToString("O") }
            }
        });

        // Wait for the employee event to be processed before sending time entry.
        // The NetPayProcessor silently drops time entries if employee info isn't in the store yet.
        await Task.Delay(5000);

        // Produce time entry (8 hours) — use current time so it lands in the same pay period
        var timeEntryId = Guid.NewGuid().ToString();
        var clockIn = DateTime.UtcNow.AddHours(-8);
        var clockOut = DateTime.UtcNow;
        await producer.ProduceAsync("employee-events", employeeId, new
        {
            Id = timeEntryId,
            EmployeeId = employeeId,
            ClockIn = clockIn.ToString("O"),
            ClockOut = clockOut.ToString("O"),
            HoursWorked = 8.0,
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            DomainEvents = new[]
            {
                new { EventType = "timeentry.created", EventId = Guid.NewGuid().ToString(), OccurredOn = DateTime.UtcNow.ToString("O") }
            }
        });

        // Wait until we see a message with TOTAL_HOURS_WORKED > 0 for this employee,
        // meaning the time entry has been processed
        using var consumer = new TopicConsumer(_fixture.BootstrapServers, $"test-hourly-{Guid.NewGuid():N}");
        consumer.Subscribe("employee-net-pay");

        var results = await consumer.ConsumeUntilAsync(
            messages => messages.Any(m =>
                m.Key.Contains(employeeId) &&
                m.Value.RootElement.GetProperty("TOTAL_HOURS_WORKED").GetDouble() > 0),
            TimeSpan.FromSeconds(60));

        var matchingMessages = results.Where(m =>
            m.Key.Contains(employeeId) &&
            m.Value.RootElement.GetProperty("TOTAL_HOURS_WORKED").GetDouble() > 0).ToList();

        matchingMessages.Should().NotBeEmpty(
            "expected at least one message with TOTAL_HOURS_WORKED > 0 for employee {0}", employeeId);

        var grossPay = matchingMessages.Last().Value.RootElement.GetProperty("GROSS_PAY").GetDouble();

        // Gross = 28.50 * 8 = 228.00
        grossPay.Should().BeApproximately(228.0, 0.1);
    }
}
