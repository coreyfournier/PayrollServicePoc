using System.Text.Json;
using ListenerApi.Data.DbContext;
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
    private readonly ListenerDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TransferController> _logger;

    private const string TransferRequestsTopic = "transfer-requests";
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(3);

    public TransferController(
        ITransferRecordRepository transferRepository,
        IEmployeeRecordRepository employeeRepository,
        ListenerDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        ILogger<TransferController> logger)
    {
        _transferRepository = transferRepository;
        _employeeRepository = employeeRepository;
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Initiates a transfer request. First attempts to validate rules against TransferService
    /// for immediate feedback. If TransferService is unavailable or times out, the command is
    /// sent via the Debezium outbox and rules are checked when the actor processes it.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TransferRecord>> InitiateTransfer([FromBody] InitiateTransferRequest request)
    {
        if (request.Amount <= 0)
            return BadRequest("Transfer amount must be positive.");

        var employee = await _employeeRepository.GetByIdAsync(request.EmployeeId);
        if (employee == null)
            return NotFound("Employee not found.");

        // Attempt authoritative validation from TransferService (single source of truth for rules).
        // If TransferService is down or slow, fall through to the outbox path — the actor will
        // enforce the same rules when it processes the command.
        var validation = await ValidateWithTransferServiceAsync(request);
        if (validation is { Responded: true, CanTransfer: false })
            return BadRequest(new { canTransfer = false, reasons = validation.Reasons });

        var transferId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Single MySQL transaction: TransferRecord + OutboxMessage (atomic)
        // Debezium CDC picks up the OutboxMessage via binlog and publishes to Kafka.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();

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
        _dbContext.TransferRecords.Add(record);

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = request.EmployeeId.ToString(),
            Topic = TransferRequestsTopic,
            Payload = JsonSerializer.Serialize(new
            {
                TransferId = transferId,
                request.EmployeeId,
                request.Amount,
                request.PayPeriodNumber,
                request.BankAccountId
            }),
            CreatedAt = now
        };
        _dbContext.OutboxMessages.Add(outboxMessage);

        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        _logger.LogInformation(
            "Transfer {TransferId} queued atomically with outbox message {OutboxId} for employee {EmployeeId} (pre-validated: {PreValidated})",
            transferId, outboxMessage.Id, request.EmployeeId, validation?.Responded == true);

        return Accepted(record);
    }

    /// <summary>
    /// Accept or reject a balance change for a transfer awaiting confirmation.
    /// Uses the Debezium Outbox Pattern — a single MySQL transaction atomically updates
    /// the TransferRecord status and writes an OutboxMessage command. Debezium CDC publishes
    /// the command to Kafka, where TransferService picks it up and raises the workflow event.
    /// </summary>
    [HttpPost("{id:guid}/accept")]
    public async Task<ActionResult> AcceptBalanceChange(Guid id, [FromBody] AcceptBalanceChangeRequest request)
    {
        var transfer = await _transferRepository.GetByIdAsync(id);
        if (transfer == null)
            return NotFound("Transfer not found.");

        if (transfer.Status != "AwaitingConfirmation")
            return BadRequest($"Transfer is not awaiting confirmation (current status: {transfer.Status}).");

        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();

        // Update read model to provide immediate feedback
        transfer.Status = request.Accepted ? "AcceptPending" : "RejectPending";
        transfer.UpdatedAt = now;
        _dbContext.TransferRecords.Update(transfer);

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = transfer.EmployeeId.ToString(),
            Topic = TransferRequestsTopic,
            Payload = JsonSerializer.Serialize(new
            {
                Action = "accept-balance",
                TransferId = id,
                transfer.EmployeeId,
                Accepted = request.Accepted
            }),
            CreatedAt = now
        };
        _dbContext.OutboxMessages.Add(outboxMessage);

        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        _logger.LogInformation(
            "Transfer {TransferId} balance {Action} queued via outbox {OutboxId}",
            id, request.Accepted ? "accept" : "reject", outboxMessage.Id);

        return Ok(transfer);
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

    private async Task<ValidationResponse> ValidateWithTransferServiceAsync(InitiateTransferRequest request)
    {
        try
        {
            using var cts = new CancellationTokenSource(ValidationTimeout);
            var client = _httpClientFactory.CreateClient("TransferService");

            var payload = JsonSerializer.Serialize(new
            {
                request.EmployeeId,
                request.Amount,
                request.PayPeriodNumber,
                request.BankAccountId
            });

            var response = await client.PostAsync(
                "/api/transfers/validate",
                new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
                cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cts.Token);
                var result = JsonSerializer.Deserialize<ValidationResponseBody>(body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result != null)
                {
                    return new ValidationResponse(true, result.CanTransfer, result.Reasons ?? new List<string>());
                }
            }

            _logger.LogWarning(
                "TransferService validation returned {StatusCode} — falling back to outbox path",
                response.StatusCode);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("TransferService validation timed out after {Timeout}ms — falling back to outbox path",
                ValidationTimeout.TotalMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "TransferService validation unavailable — falling back to outbox path");
        }

        return new ValidationResponse(false, true, new List<string>());
    }

    private async Task<TransferLimitsResponse> CheckLimitsAsync(Guid employeeId, long payPeriodNumber, decimal requestedAmount)
    {
        // These match the payroll-api defaults — in production these would be fetched from a shared config
        const int maxPerPayPeriod = 5;
        const decimal maxAmountPerPayPeriod = 10000m;
        const int maxPerDay = 1;
        var inProgressStatuses = new[] { "Initiated", "Processing", "AwaitingConfirmation", "Queued", "AcceptPending" };

        var periodTransfers = await _transferRepository.GetByEmployeeAndPayPeriodAsync(employeeId, payPeriodNumber);
        var currentCount = periodTransfers.Count;
        var currentAmount = periodTransfers.Sum(t => t.Amount);

        var todayStart = DateTime.UtcNow.Date;
        var transfersToday = await _transferRepository.GetCountByEmployeeAndDateAsync(employeeId, todayStart);

        var reasons = new List<string>();

        // Best-effort in-progress check (MySQL may lag behind MongoDB)
        var allTransfers = await _transferRepository.GetByEmployeeIdAsync(employeeId);
        var hasInProgress = allTransfers.Any(t => inProgressStatuses.Contains(t.Status));
        if (hasInProgress)
            reasons.Add("A transfer is already in progress for this employee.");

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

record ValidationResponse(bool Responded, bool CanTransfer, List<string> Reasons);
record ValidationResponseBody(bool CanTransfer, List<string>? Reasons);
