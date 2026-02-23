using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PayrollService.Api.Auth;
using PayrollService.Application.Commands.Employee;
using PayrollService.Infrastructure;
using PayrollService.Infrastructure.Persistence;
using PayrollService.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Payroll Service API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter your token below.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Add MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateEmployeeCommand).Assembly));

// Add Dapr client
builder.Services.AddDaprClient();

// Authentication — Keycloak JWT or NoAuth depending on feature flag
var useKeycloakAuth = builder.Configuration.GetValue<bool>("Features:UseKeycloakAuth", true);

if (useKeycloakAuth)
{
    var keycloakAuthority = builder.Configuration.GetValue<string>("Keycloak:Authority")
        ?? "http://localhost:8180/realms/payroll-pro";
    var keycloakAudience = builder.Configuration.GetValue<string>("Keycloak:Audience")
        ?? "payroll-api";

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = keycloakAuthority;
            options.RequireHttpsMetadata = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                // Keycloak tokens may have issuer http://keycloak:8180/... (Docker-internal)
                // or http://localhost:8180/... (browser-facing). Accept both.
                ValidIssuers = new[]
                {
                    keycloakAuthority,
                    keycloakAuthority.Replace("keycloak:8180", "localhost:8180")
                },
                ValidAudiences = new[] { keycloakAudience, "account" },
                ValidateAudience = true
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

// Add Infrastructure services
var mongoConnectionString = builder.Configuration.GetValue<string>("MongoDB:ConnectionString") ?? "mongodb://localhost:27017";
var mongoDatabaseName = builder.Configuration.GetValue<string>("MongoDB:DatabaseName") ?? "payroll_db";
var useDaprOutbox = builder.Configuration.GetValue<bool>("Features:UseDaprOutbox");
builder.Services.AddInfrastructure(mongoConnectionString, mongoDatabaseName, useDaprOutbox);

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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
