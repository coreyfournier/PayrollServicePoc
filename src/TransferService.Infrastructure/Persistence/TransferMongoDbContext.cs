using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using TransferService.Domain.Common;
using TransferService.Domain.Entities;
using TransferService.Domain.Enums;
using TransferService.Domain.Events;

namespace TransferService.Infrastructure.Persistence;

public class TransferMongoDbContext
{
    private readonly IMongoDatabase _database;
    private static bool _serializersRegistered = false;
    private static readonly object _lock = new();

    public TransferMongoDbContext(string connectionString, string databaseName)
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

            if (!BsonClassMap.IsClassMapRegistered(typeof(DomainEvent)))
            {
                BsonClassMap.RegisterClassMap<DomainEvent>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIsRootClass(true);
                });
            }

            // Transfer events
            RegisterClassMap<TransferInitiatedEvent>();
            RegisterClassMap<TransferProcessingEvent>();
            RegisterClassMap<TransferCompletedEvent>();
            RegisterClassMap<TransferFailedEvent>();
            RegisterClassMap<TransferBalanceChangedEvent>();

            // Bank account events
            RegisterClassMap<BankAccountCreatedEvent>();
            RegisterClassMap<BankAccountUpdatedEvent>();
            RegisterClassMap<BankAccountDeactivatedEvent>();

            // Register GuidSerializer with Standard representation so LINQ filters work
            BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

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

    public IMongoCollection<Transfer> Transfers => _database.GetCollection<Transfer>("transfers");
    public IMongoCollection<BankAccount> BankAccounts => _database.GetCollection<BankAccount>("bank_accounts");
    public IMongoCollection<EmployeeTransferLimits> EmployeeTransferLimits => _database.GetCollection<EmployeeTransferLimits>("employee_transfer_limits");

    public async Task InitializeAsync()
    {
        await Transfers.Indexes.CreateManyAsync(new[]
        {
            new CreateIndexModel<Transfer>(
                Builders<Transfer>.IndexKeys
                    .Ascending(t => t.EmployeeId)
                    .Ascending(t => t.PayPeriodNumber)),
            new CreateIndexModel<Transfer>(
                Builders<Transfer>.IndexKeys
                    .Ascending(t => t.EmployeeId)
                    .Ascending(t => t.InitiatedAt)),
            new CreateIndexModel<Transfer>(
                Builders<Transfer>.IndexKeys.Ascending(t => t.EmployeeId),
                new CreateIndexOptions<Transfer>
                {
                    Unique = true,
                    Name = "unique_employee_in_progress_transfer",
                    PartialFilterExpression = Builders<Transfer>.Filter.In(
                        t => t.Status,
                        new[] { TransferStatus.Initiated, TransferStatus.Processing, TransferStatus.AwaitingConfirmation })
                })
        });

        await BankAccounts.Indexes.CreateOneAsync(
            new CreateIndexModel<BankAccount>(Builders<BankAccount>.IndexKeys.Ascending(a => a.EmployeeId)));

        await EmployeeTransferLimits.Indexes.CreateOneAsync(
            new CreateIndexModel<EmployeeTransferLimits>(
                Builders<EmployeeTransferLimits>.IndexKeys.Ascending(l => l.EmployeeId),
                new CreateIndexOptions { Unique = true }));
    }
}
