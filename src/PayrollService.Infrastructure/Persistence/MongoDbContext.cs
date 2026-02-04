using MongoDB.Driver;
using PayrollService.Domain.Entities;

namespace PayrollService.Infrastructure.Persistence;

public class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public MongoDbContext(string connectionString, string databaseName)
    {
        var client = new MongoClient(connectionString);
        _database = client.GetDatabase(databaseName);
    }

    public IMongoCollection<Employee> Employees => _database.GetCollection<Employee>("employees");
    public IMongoCollection<TimeEntry> TimeEntries => _database.GetCollection<TimeEntry>("time_entries");
    public IMongoCollection<TaxInformation> TaxInformation => _database.GetCollection<TaxInformation>("tax_information");
    public IMongoCollection<Deduction> Deductions => _database.GetCollection<Deduction>("deductions");
    public IMongoCollection<OutboxMessage> OutboxMessages => _database.GetCollection<OutboxMessage>("outbox_messages");

    public async Task InitializeAsync()
    {
        // Create indexes for better query performance
        await Employees.Indexes.CreateOneAsync(
            new CreateIndexModel<Employee>(Builders<Employee>.IndexKeys.Ascending(e => e.Email)));

        await TimeEntries.Indexes.CreateOneAsync(
            new CreateIndexModel<TimeEntry>(Builders<TimeEntry>.IndexKeys.Ascending(t => t.EmployeeId)));

        await TaxInformation.Indexes.CreateOneAsync(
            new CreateIndexModel<TaxInformation>(Builders<TaxInformation>.IndexKeys.Ascending(t => t.EmployeeId)));

        await Deductions.Indexes.CreateOneAsync(
            new CreateIndexModel<Deduction>(Builders<Deduction>.IndexKeys.Ascending(d => d.EmployeeId)));

        await OutboxMessages.Indexes.CreateOneAsync(
            new CreateIndexModel<OutboxMessage>(Builders<OutboxMessage>.IndexKeys.Ascending(o => o.ProcessedAt)));
    }
}

public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = string.Empty;
    public string EventData { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}
