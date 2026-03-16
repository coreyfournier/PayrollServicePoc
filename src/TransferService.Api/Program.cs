using Confluent.Kafka;
using MassTransit;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using TransferService.Api.Consumers;
using TransferService.Api.Sagas;
using TransferService.Application.Commands.BankAccount;
using TransferService.Application.Options;
using TransferService.Infrastructure;
using TransferService.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Transfer Service API", Version = "v1" });
});

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateBankAccountCommand).Assembly));

builder.Services.Configure<TransferLimitsOptions>(
    builder.Configuration.GetSection(TransferLimitsOptions.SectionName));

var mongoConnectionString = builder.Configuration.GetValue<string>("MongoDB:ConnectionString") ?? "mongodb://localhost:27017";
var mongoDatabaseName = builder.Configuration.GetValue<string>("MongoDB:DatabaseName") ?? "transfer_db";
builder.Services.AddTransferInfrastructure(mongoConnectionString, mongoDatabaseName);

// Register IMongoDatabase for saga state queries in the controller
builder.Services.AddSingleton<IMongoDatabase>(sp =>
{
    var client = new MongoClient(mongoConnectionString);
    return client.GetDatabase(mongoDatabaseName);
});

var kafkaBootstrapServers = builder.Configuration.GetValue<string>("Kafka:BootstrapServers") ?? "kafka:9092";

// Register Confluent.Kafka producer for direct Kafka publishing (CloudEvent format)
builder.Services.AddSingleton<Confluent.Kafka.IProducer<string, string>>(sp =>
{
    var config = new Confluent.Kafka.ProducerConfig { BootstrapServers = kafkaBootstrapServers };
    return new Confluent.Kafka.ProducerBuilder<string, string>(config).Build();
});

// Register BSON serializers (including GuidSerializer) before MassTransit accesses MongoDB
TransferMongoDbContext.EnsureSerializersRegistered();

// Map TransferState so MassTransit saga repo uses CorrelationId as _id
if (!BsonClassMap.IsClassMapRegistered(typeof(TransferState)))
{
    BsonClassMap.RegisterClassMap<TransferState>(cm =>
    {
        cm.AutoMap();
        cm.MapIdProperty(x => x.CorrelationId);
    });
}

builder.Services.AddMassTransit(x =>
{
    x.AddSagaStateMachine<TransferStateMachine, TransferState>()
        .MongoDbRepository(r =>
        {
            r.Connection = mongoConnectionString;
            r.DatabaseName = mongoDatabaseName;
            r.CollectionName = "transfer_sagas";
        });

    x.UsingInMemory((context, cfg) =>
    {
        cfg.ConfigureEndpoints(context);
    });

    x.AddRider(rider =>
    {
        rider.AddConsumer<TransferRequestConsumer>();

        rider.UsingKafka((context, k) =>
        {
            k.Host(kafkaBootstrapServers);

            k.TopicEndpoint<string, TransferRequestMessage>("transfer-requests", "transfer-service-group", e =>
            {
                e.SetKeyDeserializer(Confluent.Kafka.Deserializers.Utf8);
                e.SetValueDeserializer(new RawStringDeserializer<TransferRequestMessage>(
                    (msg, val) => msg.Value = val));
                e.ConfigureConsumer<TransferRequestConsumer>(context);
            });
        });
    });
});

// Confluent Kafka producer for transfer-events (same pattern as PayrollService)
builder.Services.AddSingleton<IProducer<string, string>>(sp =>
{
    var config = new ProducerConfig { BootstrapServers = kafkaBootstrapServers };
    return new ProducerBuilder<string, string>(config).Build();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<TransferMongoDbContext>();
    await dbContext.InitializeAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Transfer Service API v1"));
}

app.UseCors();
app.MapControllers();

app.Run();
