using ListenerApi.Data.DbContext;
using ListenerApi.Data.Repositories;
using ListenerApi.Data.Services;
using ListenerApi.GraphQL.Mutations;
using ListenerApi.GraphQL.Queries;
using ListenerApi.GraphQL.Subscriptions;
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

// Controllers + Dapr
// Dapr outbox publishes CloudEvents with datacontenttype=text/plain (Dapr bug),
// so the JSON formatter must also accept text/plain to avoid HTTP 415.
builder.Services.AddControllers(options =>
{
    var jsonFormatter = options.InputFormatters
        .OfType<Microsoft.AspNetCore.Mvc.Formatters.SystemTextJsonInputFormatter>()
        .First();
    jsonFormatter.SupportedMediaTypes.Add("text/plain");
}).AddDapr();
builder.Services.AddDaprClient();

// EF Core + MySQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ListenerDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0))));

// Repositories and services
builder.Services.AddScoped<IEmployeeRecordRepository, EmployeeRecordRepository>();
builder.Services.AddScoped<EventProcessor>();
builder.Services.AddScoped<ISubscriptionPublisher, InMemorySubscriptionPublisher>();

// GraphQL
builder.Services
    .AddGraphQLServer()
    .AddQueryType<EmployeeQuery>()
    .AddMutationType<EmployeeMutation>()
    .AddSubscriptionType<EmployeeSubscription>()
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
app.UseCloudEvents();

app.MapControllers();
app.MapSubscribeHandler();
app.MapGraphQL();

app.Run();
