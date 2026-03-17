using Confluent.Kafka;
using ListenerApi.Consumers;
using ListenerApi.Data.DbContext;
using ListenerApi.Data.Repositories;
using ListenerApi.Data.Services;
using ListenerApi.GraphQL.Mutations;
using ListenerApi.GraphQL.Queries;
using ListenerApi.GraphQL.Subscriptions;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:3001")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Controllers (no Dapr)
builder.Services.AddControllers();

// EF Core + MySQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ListenerDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0))));

// Repositories and services
builder.Services.AddScoped<IEmployeeRecordRepository, EmployeeRecordRepository>();
builder.Services.AddScoped<IEmployeePayAttributesRepository, EmployeePayAttributesRepository>();
builder.Services.AddScoped<EventProcessor>();
builder.Services.AddScoped<ITransferRecordRepository, TransferRecordRepository>();
builder.Services.AddScoped<IEmployeeTransferStatusRepository, EmployeeTransferStatusRepository>();
builder.Services.AddScoped<ISubscriptionPublisher, InMemorySubscriptionPublisher>();

builder.Services.AddHttpClient("TransferService", client =>
{
    client.BaseAddress = new Uri("http://transfer-api:5002");
});

// MassTransit with Kafka Rider for consuming raw (non-MassTransit) messages
// from employee-events, transfer-events, and employee-net-pay topics.
var kafkaBootstrapServers = builder.Configuration.GetValue<string>("Kafka:BootstrapServers") ?? "kafka:9092";

builder.Services.AddMassTransit(x =>
{
    x.UsingInMemory((context, cfg) =>
    {
        cfg.ConfigureEndpoints(context);
    });

    x.AddRider(rider =>
    {
        rider.AddConsumer<EmployeeEventConsumer>();
        rider.AddConsumer<TransferEventConsumer>();
        rider.AddConsumer<NetPayEventConsumer>();
        rider.AddConsumer<TransferLimitsConsumer>();

        rider.UsingKafka((context, k) =>
        {
            k.Host(kafkaBootstrapServers);

            // employee-events: CloudEvent-wrapped JSON from PayrollService's outbox
            k.TopicEndpoint<Ignore, EmployeeEventMessage>("employee-events", "listener-api-group", e =>
            {
                e.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
                e.SetValueDeserializer(new RawStringDeserializer<EmployeeEventMessage>(
                    (msg, val) => msg.Value = val));
                e.ConfigureConsumer<EmployeeEventConsumer>(context);
            });

            // transfer-events: CloudEvent-wrapped JSON from TransferService's outbox
            k.TopicEndpoint<Ignore, TransferEventMessage>("transfer-events", "listener-api-group", e =>
            {
                e.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
                e.SetValueDeserializer(new RawStringDeserializer<TransferEventMessage>(
                    (msg, val) => msg.Value = val));
                e.ConfigureConsumer<TransferEventConsumer>(context);
            });

            // employee-net-pay: raw JSON from Java NetPayProcessor (may also be CloudEvent-wrapped)
            k.TopicEndpoint<Ignore, NetPayEventMessage>("employee-net-pay", "listener-api-group", e =>
            {
                e.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
                e.SetValueDeserializer(new RawStringDeserializer<NetPayEventMessage>(
                    (msg, val) => msg.Value = val));
                e.ConfigureConsumer<NetPayEventConsumer>(context);
            });

            // transfer-limits: JSON from transfer-api (limits per employee)
            k.TopicEndpoint<Ignore, TransferLimitsMessage>("transfer-limits", "listener-api-group", e =>
            {
                e.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
                e.SetValueDeserializer(new RawStringDeserializer<TransferLimitsMessage>(
                    (msg, val) => msg.Value = val));
                e.ConfigureConsumer<TransferLimitsConsumer>(context);
            });
        });
    });
});

// GraphQL
builder.Services
    .AddGraphQLServer()
    .AddQueryType<EmployeeQuery>()
    .AddTypeExtension<TransferQuery>()
    .AddMutationType<EmployeeMutation>()
    .AddSubscriptionType<EmployeeSubscription>()
    .AddTypeExtension<TransferSubscription>()
    .AddTypeExtension<TransferStatusSubscription>()
    .AddInMemorySubscriptions()
    .AddFiltering()
    .AddSorting();

var app = builder.Build();

// Apply EF Core migrations on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
    try
    {
        await dbContext.Database.MigrateAsync();
        app.Logger.LogInformation("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error applying database migrations");
        throw;
    }
}

app.UseCors();

app.UseRouting();
app.UseWebSockets();

app.MapControllers();
app.MapGraphQL();

app.Run();
