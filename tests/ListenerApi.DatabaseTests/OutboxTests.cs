// tests/ListenerApi.DatabaseTests/OutboxTests.cs
using System.Text.Json;
using ListenerApi.Data.DbContext;
using ListenerApi.Data.Entities;
using ListenerApi.DatabaseTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace ListenerApi.DatabaseTests;

[Collection("ListenerMySql")]
public class OutboxTests
{
    private readonly MySqlFixture _fixture;

    public OutboxTests(MySqlFixture fixture) => _fixture = fixture;

    private async Task<Guid> SeedEmployeeAsync(ListenerDbContext dbContext)
    {
        var employeeId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        dbContext.EmployeeRecords.Add(new EmployeeRecord
        {
            Id = employeeId,
            FirstName = "Test",
            LastName = "Employee",
            Email = "test@example.com",
            PayType = "2",
            PayRate = 75000m,
            IsActive = true,
            LastEventType = "employee.created",
            LastEventTimestamp = now,
            LastEventId = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();
        return employeeId;
    }

    [Fact]
    public async Task AtomicWrite_TransferRecordAndOutboxMessage_BothPersist()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();

        var employeeId = await SeedEmployeeAsync(dbContext);
        var transferId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var record = new TransferRecord
        {
            Id = transferId,
            EmployeeId = employeeId,
            Amount = 500m,
            PayPeriodNumber = 55,
            Status = "Queued",
            InitiatedAt = now,
            UpdatedAt = now
        };
        dbContext.TransferRecords.Add(record);

        var outbox = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = employeeId.ToString(),
            Topic = "transfer-requests",
            Payload = JsonSerializer.Serialize(new
            {
                TransferId = transferId,
                EmployeeId = employeeId,
                Amount = 500m,
                PayPeriodNumber = 55,
                BankAccountId = Guid.NewGuid()
            }),
            CreatedAt = now
        };
        dbContext.OutboxMessages.Add(outbox);

        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        // Verify both were persisted
        var savedRecord = await dbContext.TransferRecords.FindAsync(transferId);
        savedRecord.Should().NotBeNull();
        savedRecord!.Status.Should().Be("Queued");

        var savedOutbox = await dbContext.OutboxMessages.FindAsync(outbox.Id);
        savedOutbox.Should().NotBeNull();
        savedOutbox!.Topic.Should().Be("transfer-requests");
    }

    [Fact]
    public async Task RolledBackTransaction_NeitherPersists()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();

        var employeeId = await SeedEmployeeAsync(dbContext);
        var transferId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        dbContext.TransferRecords.Add(new TransferRecord
        {
            Id = transferId,
            EmployeeId = employeeId,
            Amount = 100m,
            PayPeriodNumber = 55,
            Status = "Queued",
            InitiatedAt = now,
            UpdatedAt = now
        });

        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            Id = outboxId,
            AggregateId = employeeId.ToString(),
            Topic = "transfer-requests",
            Payload = "{}",
            CreatedAt = now
        });

        await dbContext.SaveChangesAsync();
        await transaction.RollbackAsync();

        // Use fresh context to verify nothing persisted
        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ListenerDbContext>();

        var record = await verifyDb.TransferRecords.FindAsync(transferId);
        record.Should().BeNull("transaction was rolled back");

        var outbox = await verifyDb.OutboxMessages.FindAsync(outboxId);
        outbox.Should().BeNull("transaction was rolled back");
    }

    [Fact]
    public async Task OutboxMessage_HasCorrectTopicAndAggregateId()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();

        var employeeId = Guid.NewGuid();
        var outbox = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = employeeId.ToString(),
            Topic = "transfer-requests",
            Payload = JsonSerializer.Serialize(new { EmployeeId = employeeId }),
            CreatedAt = DateTime.UtcNow
        };
        dbContext.OutboxMessages.Add(outbox);
        await dbContext.SaveChangesAsync();

        var saved = await dbContext.OutboxMessages.FindAsync(outbox.Id);
        saved!.Topic.Should().Be("transfer-requests");
        saved.AggregateId.Should().Be(employeeId.ToString());
    }

    [Fact]
    public async Task OutboxMessage_PayloadIsValidJson()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();

        var transferId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var bankAccountId = Guid.NewGuid();

        var payload = JsonSerializer.Serialize(new
        {
            TransferId = transferId,
            EmployeeId = employeeId,
            Amount = 250m,
            PayPeriodNumber = 55,
            BankAccountId = bankAccountId
        });

        var outbox = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = employeeId.ToString(),
            Topic = "transfer-requests",
            Payload = payload,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.OutboxMessages.Add(outbox);
        await dbContext.SaveChangesAsync();

        var saved = await dbContext.OutboxMessages.FindAsync(outbox.Id);
        var parsed = JsonDocument.Parse(saved!.Payload);
        parsed.RootElement.GetProperty("TransferId").GetGuid().Should().Be(transferId);
        parsed.RootElement.GetProperty("EmployeeId").GetGuid().Should().Be(employeeId);
        parsed.RootElement.GetProperty("Amount").GetDecimal().Should().Be(250m);
        parsed.RootElement.GetProperty("PayPeriodNumber").GetInt64().Should().Be(55);
        parsed.RootElement.GetProperty("BankAccountId").GetGuid().Should().Be(bankAccountId);
    }
}
