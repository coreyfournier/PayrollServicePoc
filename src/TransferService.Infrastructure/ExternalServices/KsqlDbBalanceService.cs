using System.Text.Json;
using TransferService.Application.Interfaces;

namespace TransferService.Infrastructure.ExternalServices;

public class KsqlDbBalanceService : IBalanceService
{
    private readonly HttpClient _httpClient;

    public KsqlDbBalanceService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<BalanceInfo?> GetCurrentBalanceAsync(Guid employeeId, long payPeriodNumber)
    {
        try
        {
            var query = $"SELECT NET_PAY FROM EMPLOYEE_NET_PAY_BY_PERIOD WHERE EMPLOYEE_ID = '{employeeId}' AND PAY_PERIOD_NUMBER = {payPeriodNumber};";
            var request = new { ksql = query };

            var response = await _httpClient.PostAsync("/query",
                new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/vnd.ksql.v1+json"));

            if (!response.IsSuccessStatusCode) return null;

            var content = await response.Content.ReadAsStringAsync();
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("row", out var row) &&
                    row.TryGetProperty("columns", out var columns) &&
                    columns.GetArrayLength() > 0)
                {
                    var netPay = columns[0].GetDecimal();
                    return new BalanceInfo(netPay, payPeriodNumber);
                }
            }
        }
        catch
        {
            // Fail open — if ksqlDB is unavailable, allow the transfer
        }

        return null;
    }
}
