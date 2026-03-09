using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PayrollService.Application.Interfaces;

namespace PayrollService.Infrastructure.ExternalServices;

/// <summary>
/// Queries ksqlDB's EMPLOYEE_NET_PAY_BY_PERIOD table for current balance.
/// </summary>
public class KsqlDbBalanceService : IBalanceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<KsqlDbBalanceService> _logger;

    public KsqlDbBalanceService(HttpClient httpClient, ILogger<KsqlDbBalanceService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<BalanceInfo?> GetCurrentBalanceAsync(Guid employeeId, long payPeriodNumber, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new
            {
                ksql = $"SELECT NET_PAY FROM EMPLOYEE_NET_PAY_BY_PERIOD WHERE EMPLOYEE_ID = '{employeeId}' AND PAY_PERIOD_NUMBER = {payPeriodNumber};",
                streamsProperties = new Dictionary<string, string>()
            };

            var response = await _httpClient.PostAsJsonAsync("/query", query, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ksqlDB query failed with status {StatusCode}", response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var rows = JsonSerializer.Deserialize<List<JsonElement>>(content, options);

            if (rows == null || rows.Count < 2)
                return null;

            // First element is the header, second is the data row
            // ksqlDB pull query returns [{"header":...}, {"row":{"columns":[netPay]}}]
            for (int i = 1; i < rows.Count; i++)
            {
                if (rows[i].TryGetProperty("row", out var rowElement) &&
                    rowElement.TryGetProperty("columns", out var columns) &&
                    columns.GetArrayLength() > 0)
                {
                    var netPay = columns[0].GetDecimal();
                    return new BalanceInfo(netPay, payPeriodNumber);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query ksqlDB for balance of employee {EmployeeId}, period {PayPeriodNumber}",
                employeeId, payPeriodNumber);
            return null;
        }
    }
}
