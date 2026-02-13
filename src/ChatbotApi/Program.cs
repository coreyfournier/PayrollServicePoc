using Anthropic.SDK;
using ChatbotApi.Services;
using ChatbotApi.Tools;

var builder = WebApplication.CreateBuilder(args);

// CORS
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
builder.Services.AddSwaggerGen();

// Anthropic client
var apiKey = builder.Configuration["ANTHROPIC_API_KEY"]
    ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
    ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not configured");
builder.Services.AddSingleton(new AnthropicClient(apiKey));

// Payroll API HttpClient
var payrollBaseUrl = builder.Configuration["PayrollApi:BaseUrl"] ?? "http://localhost:5000";
builder.Services.AddHttpClient<IPayrollApiClient, PayrollApiClient>(client =>
{
    client.BaseAddress = new Uri(payrollBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Services
builder.Services.AddScoped<IEwaBalanceCalculator, EwaBalanceCalculator>();
builder.Services.AddScoped<ToolExecutor>();
builder.Services.AddScoped<IChatService, ChatService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.MapControllers();

app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();
