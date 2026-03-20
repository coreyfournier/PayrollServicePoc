// tests/KafkaPipeline.Tests/KsqlDbEmployeeInfoTests.cs
using System.Text.Json;
using KafkaPipeline.Tests.Fixtures;
using KafkaPipeline.Tests.Helpers;

namespace KafkaPipeline.Tests;

[Collection("KafkaPipeline")]
public class KsqlDbEmployeeInfoTests
{
    private readonly KafkaFixture _fixture;

    public KsqlDbEmployeeInfoTests(KafkaFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task EmployeeCreated_AppearsOnEmployeeInfoTopic()
    {
        var employeeId = Guid.NewGuid().ToString();
        using var producer = new CloudEventProducer(_fixture.BootstrapServers);

        await producer.ProduceAsync("employee-events", employeeId, new
        {
            Id = employeeId,
            FirstName = "Info",
            LastName = "Test",
            Email = "info.test@example.com",
            PayType = 2,
            PayRate = 60000.0,
            PayPeriodHours = 40.0,
            IsActive = true,
            HireDate = "2024-06-01T00:00:00Z",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            DomainEvents = new[]
            {
                new { EventType = "employee.created", EventId = Guid.NewGuid().ToString(), OccurredOn = DateTime.UtcNow.ToString("O") }
            }
        });

        // Consume from employee-info (ksqlDB output topic)
        using var consumer = new TopicConsumer(_fixture.BootstrapServers, $"test-info-{Guid.NewGuid():N}");
        consumer.Subscribe("employee-info");

        var results = await consumer.ConsumeUntilAsync(
            messages => messages.Any(m => m.Key.Contains(employeeId)),
            TimeSpan.FromSeconds(30));

        var infoMessage = results.Last(m => m.Key.Contains(employeeId));
        var root = infoMessage.Value.RootElement;

        // ksqlDB EMPLOYEE_INFO table materializes latest employee state
        // The exact field names depend on the ksqlDB schema (typically uppercased)
        root.TryGetProperty("FIRST_NAME", out var firstName).Should().BeTrue();
        firstName.GetString().Should().Be("Info");
    }
}
