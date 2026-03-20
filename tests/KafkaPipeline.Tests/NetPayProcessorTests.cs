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

        // Consume from employee-net-pay (wait for message that includes deductions)
        using var consumer = new TopicConsumer(_fixture.BootstrapServers, $"test-netpay-{Guid.NewGuid():N}");
        consumer.Subscribe("employee-net-pay");

        var results = await consumer.ConsumeUntilAsync(
            messages => messages.Count(m => m.Key.Contains(employeeId)) >= 3,
            TimeSpan.FromSeconds(45));

        var netPayMessage = results.Last(m => m.Key.Contains(employeeId));
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

        // Produce time entry (8 hours)
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

        // Consume — need >= 2 messages: first from employee event (gross=0), second from time entry (gross=228)
        using var consumer = new TopicConsumer(_fixture.BootstrapServers, $"test-hourly-{Guid.NewGuid():N}");
        consumer.Subscribe("employee-net-pay");

        var results = await consumer.ConsumeUntilAsync(
            messages => messages.Count(m => m.Key.Contains(employeeId)) >= 2,
            TimeSpan.FromSeconds(45));

        // Take the last message for this employee (after time entry is processed)
        var latestMessages = results.Where(m => m.Key.Contains(employeeId)).ToList();
        latestMessages.Should().HaveCountGreaterOrEqualTo(2);

        var last = latestMessages.Last();
        var grossPay = last.Value.RootElement.GetProperty("GROSS_PAY").GetDouble();

        // Gross = 28.50 * 8 = 228.00
        grossPay.Should().BeApproximately(228.0, 0.1);
    }
}
