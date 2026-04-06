using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayrollService.IntegrationTests.Infrastructure;

public class PayrollApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly HttpClient _transferHttp;
    private readonly HttpClient _listenerHttp;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public PayrollApiClient()
    {
        _http = new HttpClient { BaseAddress = new Uri(ServiceEndpoints.PayrollApi) };
        _transferHttp = new HttpClient { BaseAddress = new Uri(ServiceEndpoints.TransferApi) };
        _listenerHttp = new HttpClient { BaseAddress = new Uri(ServiceEndpoints.ListenerApi) };
    }

    // Employees
    public async Task<List<EmployeeResponse>> GetEmployeesAsync()
    {
        var resp = await _http.GetAsync("/api/employees");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<EmployeeResponse>>(JsonOptions))!;
    }

    // Bank Accounts (on listener-api)
    public async Task<List<BankAccountResponse>> GetBankAccountsAsync(Guid employeeId)
    {
        var resp = await _listenerHttp.GetAsync($"/api/bankaccounts/employee/{employeeId}");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<BankAccountResponse>>(JsonOptions))!;
    }

    // Transfers (on transfer-api)
    public async Task<TransferInitiateResponse> InitiateTransferAsync(Guid employeeId, decimal amount, long payPeriodNumber, Guid bankAccountId)
    {
        var resp = await _transferHttp.PostAsJsonAsync("/api/transfers", new
        {
            employeeId,
            amount,
            payPeriodNumber,
            bankAccountId
        });

        var body = await resp.Content.ReadFromJsonAsync<TransferInitiateResponse>(JsonOptions);
        return body!;
    }

    public async Task<List<TransferResponse>> GetTransfersByEmployeeAsync(Guid employeeId)
    {
        var resp = await _transferHttp.GetAsync($"/api/transfers/employee/{employeeId}");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<TransferResponse>>(JsonOptions))!;
    }

    public async Task<TransferResponse?> GetTransferAsync(Guid employeeId, Guid transferId)
    {
        var transfers = await GetTransfersByEmployeeAsync(employeeId);
        return transfers.FirstOrDefault(t => t.Id == transferId);
    }

    public async Task AcceptBalanceChangeAsync(Guid transferId, bool accepted)
    {
        var resp = await _transferHttp.PostAsJsonAsync($"/api/transfers/{transferId}/accept", new { accepted });
        resp.EnsureSuccessStatusCode();
    }

    public async Task<TransferLimitsResponse> GetTransferLimitsAsync(Guid employeeId, long payPeriodNumber)
    {
        var resp = await _transferHttp.GetAsync($"/api/transfers/employee/{employeeId}/limits?payPeriodNumber={payPeriodNumber}");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TransferLimitsResponse>(JsonOptions))!;
    }

    public void Dispose()
    {
        _http.Dispose();
        _transferHttp.Dispose();
        _listenerHttp.Dispose();
    }
}

// Response DTOs
public record EmployeeResponse(
    Guid Id, string FirstName, string LastName, string Email,
    int PayType, decimal PayRate, decimal PayPeriodHours, bool IsActive);

public record BankAccountResponse(
    Guid Id, Guid EmployeeId, string BankName, string AccountNumberMasked,
    string RoutingNumber, int AccountType, bool IsActive);

public record TransferInitiateResponse(bool Success, Guid? TransferId, string? ErrorMessage);

public record TransferResponse(
    Guid Id, Guid EmployeeId, decimal Amount, long PayPeriodNumber,
    int Status, Guid BankAccountId, DateTime InitiatedAt,
    DateTime? CompletedAt, string? FailureReason, string? ExternalReferenceId,
    decimal? CurrentBalance);

public record TransferLimitsResponse(
    int MaxTransfersPerPayPeriod, decimal MaxAmountPerPayPeriod, int MaxTransfersPerDay,
    int CurrentPeriodCount, decimal CurrentPeriodAmount, int TransfersToday,
    bool CanTransfer, List<string> Reasons);
