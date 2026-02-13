using System.Text.Json;
using ChatbotApi.Services;

namespace ChatbotApi.Tools;

public class ToolExecutor
{
    private readonly IPayrollApiClient _payrollApiClient;
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(IPayrollApiClient payrollApiClient, ILogger<ToolExecutor> logger)
    {
        _payrollApiClient = payrollApiClient;
        _logger = logger;
    }

    public async Task<string> ExecuteToolAsync(string toolName, string toolInput)
    {
        _logger.LogInformation("Executing tool: {ToolName} with input: {ToolInput}", toolName, toolInput);

        try
        {
            var input = string.IsNullOrEmpty(toolInput)
                ? new Dictionary<string, JsonElement>()
                : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(toolInput)
                  ?? new Dictionary<string, JsonElement>();

            return toolName switch
            {
                "get_all_employees" => await _payrollApiClient.GetAllEmployeesAsync(),
                "get_employee_by_id" => await _payrollApiClient.GetEmployeeByIdAsync(GetRequiredString(input, "employeeId")),
                "get_time_entries" => await _payrollApiClient.GetTimeEntriesAsync(GetRequiredString(input, "employeeId")),
                "get_tax_information" => await _payrollApiClient.GetTaxInformationAsync(GetRequiredString(input, "employeeId")),
                "get_deductions" => await _payrollApiClient.GetDeductionsAsync(GetRequiredString(input, "employeeId")),
                "get_ewa_balance" => await _payrollApiClient.GetEwaBalanceAsync(
                    GetRequiredString(input, "employeeId"),
                    input.TryGetValue("includeBreakdown", out var breakdown) && breakdown.GetBoolean()),
                _ => $"{{\"error\": \"Unknown tool: {toolName}\"}}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool {ToolName}", toolName);
            return $"{{\"error\": \"Tool execution failed: {ex.Message}\"}}";
        }
    }

    private static string GetRequiredString(Dictionary<string, JsonElement> input, string key)
    {
        if (!input.TryGetValue(key, out var value))
            throw new ArgumentException($"Missing required parameter: {key}");

        return value.GetString() ?? throw new ArgumentException($"Parameter '{key}' must be a non-null string");
    }
}
