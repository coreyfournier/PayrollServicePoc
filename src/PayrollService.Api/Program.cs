using PayrollService.Application.Commands.Employee;
using PayrollService.Application.Configuration;
using PayrollService.Application.Services;
using PayrollService.Infrastructure;
using PayrollService.Infrastructure.Persistence;
using PayrollService.Infrastructure.Seeding;

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
builder.Services.AddControllers().AddDapr();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Payroll Service API", Version = "v1" });
});

// Add MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateEmployeeCommand).Assembly));

// Add Dapr client
builder.Services.AddDaprClient();

// Add EWA configuration and services
builder.Services.Configure<EwaSettings>(builder.Configuration.GetSection(EwaSettings.SectionName));
builder.Services.AddScoped<EwaCalculationService>();

// Add Infrastructure services
var mongoConnectionString = builder.Configuration.GetValue<string>("MongoDB:ConnectionString") ?? "mongodb://localhost:27017";
var mongoDatabaseName = builder.Configuration.GetValue<string>("MongoDB:DatabaseName") ?? "payroll_db";
builder.Services.AddInfrastructure(mongoConnectionString, mongoDatabaseName);

var app = builder.Build();

// Initialize database and seed data
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
    await dbContext.InitializeAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedAsync();
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Payroll Service API v1"));
}

app.UseCors();
app.UseCloudEvents();
app.MapControllers();
app.MapSubscribeHandler();

app.Run();
