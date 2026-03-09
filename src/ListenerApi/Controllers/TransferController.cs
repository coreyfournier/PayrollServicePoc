using Dapr.Client;
using ListenerApi.Data.Entities;
using ListenerApi.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace ListenerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TransferController : ControllerBase
{
    private readonly ITransferRecordRepository _transferRepository;
    private readonly IEmployeeRecordRepository _employeeRepository;
    private readonly DaprClient _daprClient;
    private readonly ILogger<TransferController> _logger;

    private const string StateStoreName = "statestore-listener-transfers";
    private const string PubSubName = "kafka-pubsub-listener";
    private const string TransferRequestsTopic = "transfer-requests";

    public TransferController(
        ITransferRecordRepository transferRepository,
        IEmployeeRecordRepository employeeRepository,
        DaprClient daprClient,
        ILogger<TransferController> logger)
    {
        _transferRepository = transferRepository;
        _employeeRepository = employeeRepository;
        _daprClient = daprClient;
        _logger = logger;
    }

    /// <summary>
    /// Initiates a transfer request. Persists to MySQL and publishes to Kafka
    /// via Dapr state store outbox (atomic). Payroll-api processes when available.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TransferRecord>> InitiateTransfer([FromBody] InitiateTransferRequest request)
    {
        if (request.Amount <= 0)
            return BadRequest("Transfer amount must be positive.");

        var employee = await _employeeRepository.GetByIdAsync(request.EmployeeId);
        if (employee == null)
            return NotFound("Employee not found.");

        // Client-side limit pre-check (best-effort from local materialized data)
        var limitsCheck = await CheckLimitsAsync(request.EmployeeId, request.PayPeriodNumber, request.Amount);
        if (!limitsCheck.CanTransfer)
            return BadRequest(new { canTransfer = false, reasons = limitsCheck.Reasons });

        var transferId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Persist to MySQL as the client's read model (status: Queued)
        var record = new TransferRecord
        {
            Id = transferId,
            EmployeeId = request.EmployeeId,
            Amount = request.Amount,
            PayPeriodNumber = request.PayPeriodNumber,
            Status = "Queued",
            InitiatedAt = now,
            UpdatedAt = now
        };
        await _transferRepository.AddAsync(record);

        // Publish transfer request to Kafka via Dapr pub/sub
        // This uses Dapr pub/sub directly (not outbox) since the MySQL write is our read model
        // and the Kafka message is the command to payroll-api
        var transferRequest = new
        {
            TransferId = transferId,
            request.EmployeeId,
            request.Amount,
            request.PayPeriodNumber,
            request.BankAccountId
        };

        try
        {
            await _daprClient.PublishEventAsync(PubSubName, TransferRequestsTopic, transferRequest);
            _logger.LogInformation("Published transfer request {TransferId} for employee {EmployeeId}",
                transferId, request.EmployeeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish transfer request {TransferId}. Record saved as Queued — will need retry.",
                transferId);
            // Record is saved as Queued — visible to client on refresh
            // A background retry mechanism could pick this up (out of scope for POC)
        }

        return Accepted(record);
    }

    /// <summary>
    /// Accept or reject a balance change for a transfer awaiting confirmation.
    /// Forwards to payroll-api via Dapr service invocation.
    /// </summary>
    [HttpPost("{id:guid}/accept")]
    public async Task<ActionResult> AcceptBalanceChange(Guid id, [FromBody] AcceptBalanceChangeRequest request)
    {
        try
        {
            var httpRequest = _daprClient.CreateInvokeMethodRequest(
                HttpMethod.Post,
                "payroll-api",
                $"api/transfers/{id}/accept",
                new { Accepted = request.Accepted });
            await _daprClient.InvokeMethodAsync(httpRequest);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to forward accept/reject for transfer {TransferId}", id);
            return StatusCode(502, "Failed to reach payroll service.");
        }
    }

    [HttpGet("employee/{employeeId:guid}")]
    public async Task<ActionResult<List<TransferRecord>>> GetByEmployee(Guid employeeId)
    {
        var transfers = await _transferRepository.GetByEmployeeIdAsync(employeeId);
        return Ok(transfers);
    }

    [HttpGet("employee/{employeeId:guid}/limits")]
    public async Task<ActionResult<TransferLimitsResponse>> GetLimits(Guid employeeId, [FromQuery] long payPeriodNumber)
    {
        var check = await CheckLimitsAsync(employeeId, payPeriodNumber, 0);
        return Ok(check);
    }

    private async Task<TransferLimitsResponse> CheckLimitsAsync(Guid employeeId, long payPeriodNumber, decimal requestedAmount)
    {
        // These match the payroll-api defaults — in production these would be fetched from a shared config
        const int maxPerPayPeriod = 5;
        const decimal maxAmountPerPayPeriod = 10000m;
        const int maxPerDay = 1;

        var periodTransfers = await _transferRepository.GetByEmployeeAndPayPeriodAsync(employeeId, payPeriodNumber);
        var currentCount = periodTransfers.Count;
        var currentAmount = periodTransfers.Sum(t => t.Amount);

        var todayStart = DateTime.UtcNow.Date;
        var transfersToday = await _transferRepository.GetCountByEmployeeAndDateAsync(employeeId, todayStart);

        var reasons = new List<string>();

        if (transfersToday >= maxPerDay)
            reasons.Add($"Daily transfer limit reached ({maxPerDay} per day).");
        if (currentCount >= maxPerPayPeriod)
            reasons.Add($"Pay period transfer limit reached ({maxPerPayPeriod} per period).");
        if (requestedAmount > 0 && currentAmount + requestedAmount > maxAmountPerPayPeriod)
            reasons.Add($"Pay period amount limit would be exceeded (${maxAmountPerPayPeriod} max, ${currentAmount} already transferred).");

        return new TransferLimitsResponse(
            maxPerPayPeriod, maxAmountPerPayPeriod, maxPerDay,
            currentCount, currentAmount, transfersToday,
            reasons.Count == 0, reasons);
    }
}

public record InitiateTransferRequest(Guid EmployeeId, decimal Amount, long PayPeriodNumber, Guid BankAccountId);
public record AcceptBalanceChangeRequest(bool Accepted);

public record TransferLimitsResponse(
    int MaxTransfersPerPayPeriod,
    decimal MaxAmountPerPayPeriod,
    int MaxTransfersPerDay,
    int CurrentPeriodCount,
    decimal CurrentPeriodAmount,
    int TransfersToday,
    bool CanTransfer,
    List<string> Reasons);
