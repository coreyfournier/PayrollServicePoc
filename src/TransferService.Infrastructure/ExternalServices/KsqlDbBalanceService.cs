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
            var netPay = await QueryDecimalAsync(
                $"SELECT NET_PAY FROM EMPLOYEE_NET_PAY_BY_PERIOD WHERE EMPLOYEE_ID = '{employeeId}' AND PAY_PERIOD_NUMBER = {payPeriodNumber};",
                employeeId);

            if (netPay == null)
            {
                _logger.LogWarning("No balance data found for employee {EmployeeId}, period {PayPeriod}", employeeId, payPeriodNumber);
                return null;
            }

            var totalTransferred = await QueryDecimalAsync(
                $"SELECT TOTAL_AMOUNT FROM TRANSFER_USAGE_BY_PERIOD WHERE EMPLOYEE_ID = '{employeeId}' AND PAY_PERIOD_NUMBER = {payPeriodNumber};",
                employeeId) ?? 0m;

            _logger.LogDebug("Balance for employee {EmployeeId}: NetPay={NetPay}, TotalTransferred={TotalTransferred}, Available={Available}",
                employeeId, netPay.Value, totalTransferred, netPay.Value - totalTransferred);

            return new BalanceInfo(netPay.Value, totalTransferred, payPeriodNumber);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query ksqlDB balance for employee {EmployeeId}", employeeId);
        }

        return null;
    }

    private async Task<decimal?> QueryDecimalAsync(string ksql, Guid employeeId)
    {
        var request = new { ksql };
        var response = await _httpClient.PostAsync("/query",
            new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/vnd.ksql.v1+json"));

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("ksqlDB returned {StatusCode} for query on employee {EmployeeId}", response.StatusCode, employeeId);
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
                        return cols[0].GetDecimal();
                    }
                }
                return null; // Valid JSON array but no row data
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

            if (doc.RootElement.ValueKind == JsonValueKind.Array &&
                doc.RootElement.GetArrayLength() > 0)
            {
                return doc.RootElement[0].GetDecimal();
            }

            if (doc.RootElement.TryGetProperty("row", out var row) &&
                row.TryGetProperty("columns", out var columns) &&
                columns.GetArrayLength() > 0)
            {
                return columns[0].GetDecimal();
            }
        }

        return null;
    }
}
