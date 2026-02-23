using ListenerApi.Auth;
using ListenerApi.Data.DbContext;
using ListenerApi.Data.Repositories;
using ListenerApi.Data.Services;
using ListenerApi.GraphQL.Mutations;
using ListenerApi.GraphQL.Queries;
using ListenerApi.GraphQL.Subscriptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

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
    // Raw Kafka messages (e.g. from NetPayProcessor) arrive via Dapr with
    // datacontenttype=application/octet-stream since no content-type header is set.
    jsonFormatter.SupportedMediaTypes.Add("application/octet-stream");
}).AddDapr();
builder.Services.AddDaprClient();

// Authentication — Keycloak JWT or NoAuth depending on feature flag
var useKeycloakAuth = builder.Configuration.GetValue<bool>("Features:UseKeycloakAuth", true);

if (useKeycloakAuth)
{
    var keycloakAuthority = builder.Configuration.GetValue<string>("Keycloak:Authority")
        ?? "http://localhost:8180/realms/listener-client";
    var keycloakAudience = builder.Configuration.GetValue<string>("Keycloak:Audience")
        ?? "listener-api";

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = keycloakAuthority;
            options.RequireHttpsMetadata = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = keycloakAuthority.Replace("keycloak:8180", "localhost:8180"),
                ValidAudiences = new[] { keycloakAudience, "account" },
                ValidateAudience = true
            };
            // Extract access_token from WebSocket query string for GraphQL subscriptions
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken) &&
                        context.HttpContext.WebSockets.IsWebSocketRequest)
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        });

    builder.Services.AddTransient<IClaimsTransformation, KeycloakClaimsTransformation>();
}
else
{
    builder.Services.AddAuthentication("NoAuth")
        .AddScheme<AuthenticationSchemeOptions, NoAuthHandler>("NoAuth", null);
}

builder.Services.AddAuthorization();

// EF Core + MySQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ListenerDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0))));

// Repositories and services
builder.Services.AddScoped<IEmployeeRecordRepository, EmployeeRecordRepository>();
builder.Services.AddScoped<IEmployeePayAttributesRepository, EmployeePayAttributesRepository>();
builder.Services.AddScoped<EventProcessor>();
builder.Services.AddScoped<ISubscriptionPublisher, InMemorySubscriptionPublisher>();

// GraphQL
builder.Services
    .AddGraphQLServer()
    .AddAuthorization()
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

// Capture raw body for event subscription endpoints BEFORE UseCloudEvents() consumes it.
// Dapr outbox stringifies the JSON data field (Dapr bug #8130), so UseCloudEvents()
// produces a body that can't deserialize into our DTOs. We preserve the original
// CloudEvent body and parse it manually in the controllers.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/eventsubscription"))
    {
        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;
        context.Items["RawBody"] = body;
    }
    await next();
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseWebSockets();
app.UseCloudEvents();

// Health endpoint for Docker healthcheck (no auth required)
app.MapGet("/health", () => Results.Ok("healthy"));

// GraphQL endpoint requires authorization; Dapr subscription endpoints are open
app.MapGraphQL().RequireAuthorization();
app.MapControllers();
app.MapSubscribeHandler();

app.Run();
