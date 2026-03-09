using Dapr.Workflow;
using PayrollService.Api.Actors;
using PayrollService.Api.Workflows;
using PayrollService.Api.Workflows.Activities;
using PayrollService.Application.Commands.Employee;
using PayrollService.Application.Options;
using PayrollService.Infrastructure;
using PayrollService.Infrastructure.Persistence;

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

// Add Dapr Workflow
builder.Services.AddDaprWorkflow(options =>
{
    options.RegisterWorkflow<TransferWorkflow>();
    options.RegisterActivity<ValidateTransferActivity>();
    options.RegisterActivity<UpdateTransferStatusActivity>();
    options.RegisterActivity<ExecuteBankTransferActivity>();
    options.RegisterActivity<CompleteTransferActivity>();
    options.RegisterActivity<FailTransferActivity>();
    options.RegisterActivity<VerifyBalanceActivity>();
    options.RegisterActivity<MarkAwaitingConfirmationActivity>();
});

// Add Dapr Actors
builder.Services.AddActors(options =>
{
    options.Actors.RegisterActor<TransferActor>();
});

// Add Transfer Limits configuration
builder.Services.Configure<TransferLimitsOptions>(
    builder.Configuration.GetSection(TransferLimitsOptions.SectionName));

// Add Infrastructure services
var mongoConnectionString = builder.Configuration.GetValue<string>("MongoDB:ConnectionString") ?? "mongodb://localhost:27017";
var mongoDatabaseName = builder.Configuration.GetValue<string>("MongoDB:DatabaseName") ?? "payroll_db";
var useDaprOutbox = builder.Configuration.GetValue<bool>("Features:UseDaprOutbox");
builder.Services.AddInfrastructure(mongoConnectionString, mongoDatabaseName, useDaprOutbox);

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
    await dbContext.InitializeAsync();
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
app.MapActorsHandlers();

app.Run();
