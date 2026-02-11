using Orleans.Configuration;
using PayrollService.Application.Commands.Employee;
using PayrollService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Payroll Service API", Version = "v1" });
});

// Add MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateEmployeeCommand).Assembly));

// Configuration
var mongoConnectionString = builder.Configuration.GetValue<string>("MongoDB:ConnectionString") ?? "mongodb://localhost:27017";
var mongoDatabaseName = builder.Configuration.GetValue<string>("MongoDB:DatabaseName") ?? "payroll_db";
var kafkaBrokers = builder.Configuration.GetValue<string>("Kafka:Brokers") ?? "localhost:9092";

// Configure Orleans
builder.Host.UseOrleans(siloBuilder =>
{
    siloBuilder.UseLocalhostClustering();

    // MongoDB persistence for grain state
    siloBuilder.UseMongoDBClient(mongoConnectionString)
        .AddMongoDBGrainStorage("MongoDBStore", options =>
        {
            options.DatabaseName = mongoDatabaseName;
            options.CollectionPrefix = "orleans_";
        });

    // Configure cluster options
    siloBuilder.Configure<ClusterOptions>(options =>
    {
        options.ClusterId = "payroll-cluster";
        options.ServiceId = "PayrollService";
    });
});

// Add Infrastructure services (includes Kafka event publisher)
builder.Services.AddInfrastructure(mongoConnectionString, mongoDatabaseName, kafkaBrokers);

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Payroll Service API v1"));
}

app.UseCors();
app.MapControllers();

app.Run();
