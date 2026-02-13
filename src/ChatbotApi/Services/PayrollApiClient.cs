namespace ChatbotApi.Services;

public class PayrollApiClient : IPayrollApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PayrollApiClient> _logger;

    public PayrollApiClient(HttpClient httpClient, ILogger<PayrollApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string> GetAllEmployeesAsync()
    {
        return await GetAsync("/api/employees");
    }

    public async Task<string> GetEmployeeByIdAsync(string employeeId)
    {
        return await GetAsync($"/api/employees/{employeeId}");
    }

    public async Task<string> GetTimeEntriesAsync(string employeeId)
    {
        return await GetAsync($"/api/timeentries/employee/{employeeId}");
    }

    public async Task<string> GetTaxInformationAsync(string employeeId)
    {
        return await GetAsync($"/api/taxinformation/employee/{employeeId}");
    }

    public async Task<string> GetDeductionsAsync(string employeeId)
    {
        return await GetAsync($"/api/deductions/employee/{employeeId}");
    }

    public async Task<string> GetEwaBalanceAsync(string employeeId, bool includeBreakdown = false)
    {
        return await GetAsync($"/api/v1/employees/{employeeId}/balance?includeBreakdown={includeBreakdown}");
    }

    private async Task<string> GetAsync(string path)
    {
        _logger.LogDebug("Calling Payroll API: GET {Path}", path);
        var response = await _httpClient.GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Payroll API returned {StatusCode} for GET {Path}: {Content}",
                response.StatusCode, path, content);
            return $"{{\"error\": \"API returned {response.StatusCode}\", \"details\": {content}}}";
        }

        return content;
    }
}
