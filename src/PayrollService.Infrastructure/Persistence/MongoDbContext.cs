using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using PayrollService.Domain.Common;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Events;

namespace PayrollService.Infrastructure.Persistence;

public class MongoDbContext
{
    private readonly IMongoDatabase _database;
    private static bool _serializersRegistered = false;
    private static readonly object _lock = new();

    public MongoDbContext(string connectionString, string databaseName)
    {
        RegisterSerializers();
        var client = new MongoClient(connectionString);
        _database = client.GetDatabase(databaseName);
    }

    private static void RegisterSerializers()
    {
        lock (_lock)
        {
            if (_serializersRegistered) return;

            // Register domain event types as known types for polymorphic serialization
            if (!BsonClassMap.IsClassMapRegistered(typeof(DomainEvent)))
            {
                BsonClassMap.RegisterClassMap<DomainEvent>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIsRootClass(true);
                });
            }

            // Employee events
            RegisterClassMap<EmployeeCreatedEvent>();
            RegisterClassMap<EmployeeUpdatedEvent>();
            RegisterClassMap<EmployeeDeactivatedEvent>();
            RegisterClassMap<EmployeeActivatedEvent>();

            // Tax information events
            RegisterClassMap<TaxInformationCreatedEvent>();
            RegisterClassMap<TaxInformationUpdatedEvent>();

            // Deduction events
            RegisterClassMap<DeductionCreatedEvent>();
            RegisterClassMap<DeductionUpdatedEvent>();
            RegisterClassMap<DeductionDeactivatedEvent>();

            // Time entry events
            RegisterClassMap<EmployeeClockedInEvent>();
            RegisterClassMap<EmployeeClockedOutEvent>();
            RegisterClassMap<TimeEntryUpdatedEvent>();

            // Configure ObjectSerializer to allow all types (needed for 'object' typed properties)
            var objectSerializer = new ObjectSerializer(type => ObjectSerializer.AllAllowedTypes(type));
            BsonSerializer.RegisterSerializer(objectSerializer);

            _serializersRegistered = true;
        }
    }

    private static void RegisterClassMap<T>() where T : DomainEvent
    {
        if (!BsonClassMap.IsClassMapRegistered(typeof(T)))
        {
            BsonClassMap.RegisterClassMap<T>(cm =>
            {
                cm.AutoMap();
                cm.SetDiscriminator(typeof(T).Name);
            });
        }
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
    public required object EventData { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}
