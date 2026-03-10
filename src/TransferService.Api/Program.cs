using Dapr.Workflow;
using TransferService.Api.Actors;
using TransferService.Api.Workflows;
using TransferService.Api.Workflows.Activities;
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

builder.Services.AddControllers().AddDapr();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Transfer Service API", Version = "v1" });
});

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateBankAccountCommand).Assembly));

builder.Services.AddDaprClient();

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

builder.Services.AddActors(options =>
{
    options.Actors.RegisterActor<TransferActor>();
});

builder.Services.Configure<TransferLimitsOptions>(
    builder.Configuration.GetSection(TransferLimitsOptions.SectionName));

var mongoConnectionString = builder.Configuration.GetValue<string>("MongoDB:ConnectionString") ?? "mongodb://localhost:27017";
var mongoDatabaseName = builder.Configuration.GetValue<string>("MongoDB:DatabaseName") ?? "transfer_db";
builder.Services.AddTransferInfrastructure(mongoConnectionString, mongoDatabaseName);

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
app.UseCloudEvents();
app.MapControllers();
app.MapSubscribeHandler();
app.MapActorsHandlers();

app.Run();
