using System.Text.Json;
using Microsoft.Extensions.Logging;
using TransferService.Application.Interfaces;

namespace TransferService.Infrastructure.ExternalServices;

public class KsqlDbBalanceService : IBalanceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<KsqlDbBalanceService> _logger;

    public KsqlDbBalanceService(HttpClient httpClient, ILogger<KsqlDbBalanceService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<BalanceInfo?> GetCurrentBalanceAsync(Guid employeeId, long payPeriodNumber)
    {
        try
        {
            var query = $"SELECT NET_PAY FROM EMPLOYEE_NET_PAY_BY_PERIOD WHERE EMPLOYEE_ID = '{employeeId}' AND PAY_PERIOD_NUMBER = {payPeriodNumber};";
            var request = new { ksql = query };

            var response = await _httpClient.PostAsync("/query",
                new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/vnd.ksql.v1+json"));

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ksqlDB returned {StatusCode} for employee {EmployeeId}", response.StatusCode, employeeId);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();

            // Try parsing as JSON array first (HTTP/2 pull query format):
            // [{"header":{...}},{"row":{"columns":[1017.84]}},...]
            try
            {
                var fullDoc = JsonDocument.Parse(content);
                if (fullDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in fullDoc.RootElement.EnumerateArray())
                    {
                        if (element.TryGetProperty("row", out var rowObj) &&
                            rowObj.TryGetProperty("columns", out var cols) &&
                            cols.GetArrayLength() > 0)
                        {
                            var netPay = cols[0].GetDecimal();
                            _logger.LogDebug("Balance found: {NetPay} for employee {EmployeeId}", netPay, employeeId);
                            return new BalanceInfo(netPay, payPeriodNumber);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Not a single JSON array — try line-delimited format
            }

            // Line-delimited format (HTTP/1.1):
            // {"queryId":"...","columnNames":["NET_PAY"],...}
            // [1017.84]
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var doc = JsonDocument.Parse(line);

                // Data lines are plain arrays like [1017.84]
                if (doc.RootElement.ValueKind == JsonValueKind.Array &&
                    doc.RootElement.GetArrayLength() > 0)
                {
                    var netPay = doc.RootElement[0].GetDecimal();
                    _logger.LogDebug("Balance found: {NetPay} for employee {EmployeeId}", netPay, employeeId);
                    return new BalanceInfo(netPay, payPeriodNumber);
                }

                // Push query format: {"row": {"columns": [...]}}
                if (doc.RootElement.TryGetProperty("row", out var row) &&
                    row.TryGetProperty("columns", out var columns) &&
                    columns.GetArrayLength() > 0)
                {
                    var netPay = columns[0].GetDecimal();
                    _logger.LogDebug("Balance found: {NetPay} for employee {EmployeeId}", netPay, employeeId);
                    return new BalanceInfo(netPay, payPeriodNumber);
                }
            }

            _logger.LogWarning("No balance data found for employee {EmployeeId}, period {PayPeriod}", employeeId, payPeriodNumber);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query ksqlDB balance for employee {EmployeeId}", employeeId);
        }

        return null;
    }
}
