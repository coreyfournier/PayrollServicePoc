using System.Text.Json;
using System.Text.Json.Serialization;
using ListenerApi.Data.Entities;
using ListenerApi.Data.Repositories;
using Microsoft.Extensions.Logging;

namespace ListenerApi.Data.Services;

public class EventProcessor
{
    private readonly IEmployeeRecordRepository _repository;
    private readonly IEmployeePayAttributesRepository _payAttributesRepository;
    private readonly ITransferRecordRepository _transferRecordRepository;
    private readonly ISubscriptionPublisher _subscriptionPublisher;
    private readonly ILogger<EventProcessor> _logger;

    private static readonly Dictionary<string, int> StatusOrder = new()
    {
        ["Queued"] = 0,
        ["Initiated"] = 1,
        ["AwaitingConfirmation"] = 2,
        ["AcceptPending"] = 2,
        ["RejectPending"] = 2,
        ["Processing"] = 3,
        ["Completed"] = 4,
        ["Failed"] = 4
    };

    public EventProcessor(
        IEmployeeRecordRepository repository,
        IEmployeePayAttributesRepository payAttributesRepository,
        ITransferRecordRepository transferRecordRepository,
        ISubscriptionPublisher subscriptionPublisher,
        ILogger<EventProcessor> logger)
    {
        _repository = repository;
        _payAttributesRepository = payAttributesRepository;
        _transferRecordRepository = transferRecordRepository;
        _subscriptionPublisher = subscriptionPublisher;
        _logger = logger;
    }

    public async Task ProcessEmployeeEventAsync(EmployeeEventPayload eventData)
    {
        var (employeeId, eventId, eventType, occurredOn) = eventData.ResolveEventInfo();

        _logger.LogInformation("Processing employee event: {EventType} {EventId} for {EmployeeId}",
            eventType, eventId, employeeId);

        var existing = await _repository.GetByIdAsync(employeeId);

        // Idempotency checks
        if (existing != null)
        {
            if (existing.LastEventId == eventId)
            {
                _logger.LogInformation("Skipping duplicate event {EventId}", eventId);
                return;
            }

            if (existing.LastEventTimestamp >= occurredOn)
            {
                _logger.LogInformation("Skipping older event {EventId} - existing timestamp {ExistingTimestamp} >= incoming {IncomingTimestamp}",
                    eventId, existing.LastEventTimestamp, occurredOn);
                return;
            }
        }

        // Process based on event type
        var record = existing ?? new EmployeeRecord { Id = employeeId };

        switch (eventType)
        {
            case "employee.created":
            case "employee.updated":
                record.FirstName = eventData.FirstName;
                record.LastName = eventData.LastName;
                record.Email = eventData.Email;
                record.PayType = eventData.PayType?.ToString() ?? string.Empty;
                record.PayRate = eventData.PayRate;
                record.PayPeriodHours = eventData.PayPeriodHours ?? 40;
                record.IsActive = eventData.IsActive;
                break;
            case "employee.deactivated":
                record.IsActive = false;
                await _payAttributesRepository.DeleteByEmployeeIdAsync(employeeId);
                break;
            case "employee.activated":
                record.IsActive = true;
                break;
            default:
                _logger.LogWarning("Unknown event type {EventType}", eventType);
                return;
        }

        // Update tracking
        record.LastEventType = eventType;
        record.LastEventTimestamp = occurredOn;
        record.LastEventId = eventId;
        record.UpdatedAt = DateTime.UtcNow;

        if (existing == null)
        {
            record.CreatedAt = DateTime.UtcNow;
            await _repository.AddAsync(record);
            _logger.LogInformation("Created new employee record {EmployeeId}", record.Id);
        }
        else
        {
            await _repository.UpdateAsync(record);
            _logger.LogInformation("Updated employee record {EmployeeId}", record.Id);
        }

        // Notify GraphQL subscribers
        await _subscriptionPublisher.PublishEmployeeChangeAsync(record, eventType);
    }

    public async Task ProcessNetPayEventAsync(NetPayEventPayload eventData)
    {
        if (!Guid.TryParse(eventData.EmployeeId, out var employeeId))
        {
            _logger.LogWarning("Invalid employeeId in net pay event: {EmployeeId}", eventData.EmployeeId);
            return;
        }

        _logger.LogInformation("Processing net pay event for employee {EmployeeId}, period {PayPeriodNumber}",
            employeeId, eventData.PayPeriodNumber);

        // Idempotency: only update if incoming period >= existing
        var existing = await _payAttributesRepository.GetByEmployeeIdAsync(employeeId);
        if (existing != null && eventData.PayPeriodNumber < existing.PayPeriodNumber)
        {
            _logger.LogInformation("Skipping older net pay event for employee {EmployeeId} - existing period {ExistingPeriod} > incoming {IncomingPeriod}",
                employeeId, existing.PayPeriodNumber, eventData.PayPeriodNumber);
            return;
        }

        var payAttributes = new Entities.EmployeePayAttributes
        {
            EmployeeId = employeeId,
            PayPeriodNumber = eventData.PayPeriodNumber,
            GrossPay = (decimal)eventData.GrossPay,
            FederalTax = (decimal)eventData.FederalTax,
            StateTax = (decimal)eventData.StateTax,
            AdditionalFederalWithholding = (decimal)eventData.AdditionalFederalWithholding,
            AdditionalStateWithholding = (decimal)eventData.AdditionalStateWithholding,
            TotalTax = (decimal)eventData.TotalTax,
            TotalFixedDeductions = (decimal)eventData.TotalFixedDeductions,
            TotalPercentDeductions = (decimal)eventData.TotalPercentDeductions,
            TotalDeductions = (decimal)eventData.TotalDeductions,
            NetPay = (decimal)eventData.NetPay,
            PayRate = (decimal)eventData.PayRate,
            PayType = eventData.PayType ?? string.Empty,
            TotalHoursWorked = (decimal)eventData.TotalHoursWorked,
            PayPeriodStart = eventData.PayPeriodStart ?? string.Empty,
            PayPeriodEnd = eventData.PayPeriodEnd ?? string.Empty,
            UpdatedAt = DateTime.UtcNow
        };

        await _payAttributesRepository.UpsertAsync(payAttributes);
        _logger.LogInformation("Upserted pay attributes for employee {EmployeeId}, period {PayPeriodNumber}, netPay={NetPay}",
            employeeId, eventData.PayPeriodNumber, eventData.NetPay);

        // Notify GraphQL subscribers
        var employee = await _repository.GetByIdAsync(employeeId);
        if (employee != null)
        {
            employee.PayAttributes = payAttributes;
            await _subscriptionPublisher.PublishPayAttributesChangeAsync(employee);
        }
    }

    public async Task ProcessTransferEventAsync(TransferEventPayload eventData)
    {
        var (transferId, employeeId, eventType) = eventData.ResolveTransferInfo();

        _logger.LogInformation("Processing transfer event: {EventType} for transfer {TransferId}, employee {EmployeeId}",
            eventType, transferId, employeeId);

        var newStatus = eventType switch
        {
            "transfer.initiated" => "Initiated",
            "transfer.balance_changed" => "AwaitingConfirmation",
            "transfer.processing" => "Processing",
            "transfer.completed" => "Completed",
            "transfer.failed" => "Failed",
            _ => null
        };

        if (newStatus == null)
        {
            _logger.LogWarning("Unknown transfer event type: {EventType}", eventType);
            return;
        }

        var existing = await _transferRecordRepository.GetByIdAsync(transferId);

        if (existing != null)
        {
            // Idempotency: status only advances forward
            var existingOrder = StatusOrder.GetValueOrDefault(existing.Status, -1);
            var newOrder = StatusOrder.GetValueOrDefault(newStatus, -1);
            if (newOrder <= existingOrder)
            {
                _logger.LogInformation("Skipping transfer event — status {NewStatus} not newer than {ExistingStatus}",
                    newStatus, existing.Status);
                return;
            }

            existing.Status = newStatus;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.WorkflowStepsJson = eventData.SerializeWorkflowSteps();

            if (newStatus == "Completed")
            {
                existing.CompletedAt = DateTime.UtcNow;
                existing.ExternalReferenceId = eventData.ExternalReferenceId;
            }
            else if (newStatus == "Failed")
            {
                existing.FailureReason = eventData.FailureReason;
            }
            else if (newStatus == "AwaitingConfirmation")
            {
                existing.CurrentBalance = eventData.CurrentBalance;
            }

            await _transferRecordRepository.UpdateAsync(existing);
        }
        else
        {
            // Create new record (transfer originated from payroll-api directly)
            var record = new Entities.TransferRecord
            {
                Id = transferId,
                EmployeeId = employeeId,
                Amount = eventData.Amount,
                PayPeriodNumber = eventData.PayPeriodNumber,
                Status = newStatus,
                InitiatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CompletedAt = newStatus == "Completed" ? DateTime.UtcNow : null,
                ExternalReferenceId = eventData.ExternalReferenceId,
                FailureReason = eventData.FailureReason,
                WorkflowStepsJson = eventData.SerializeWorkflowSteps()
            };

            await _transferRecordRepository.AddAsync(record);
        }

        // Update pay attributes transfer summary on completion
        if (newStatus == "Completed")
        {
            await UpdateTransferSummaryAsync(employeeId, eventData.PayPeriodNumber);
        }

        // Notify GraphQL subscribers
        var transferRecord = await _transferRecordRepository.GetByIdAsync(transferId);
        if (transferRecord != null)
        {
            await _subscriptionPublisher.PublishTransferChangeAsync(transferRecord, eventType);
        }
    }

    private async Task UpdateTransferSummaryAsync(Guid employeeId, long payPeriodNumber)
    {
        var completedTransfers = await _transferRecordRepository.GetByEmployeeAndPayPeriodAsync(employeeId, payPeriodNumber);
        var completed = completedTransfers.Where(t => t.Status == "Completed").ToList();

        var payAttributes = await _payAttributesRepository.GetByEmployeeIdAsync(employeeId);
        if (payAttributes != null && payAttributes.PayPeriodNumber == payPeriodNumber)
        {
            payAttributes.TransferCount = completed.Count;
            payAttributes.TransferTotalAmount = completed.Sum(t => t.Amount);
            payAttributes.UpdatedAt = DateTime.UtcNow;
            await _payAttributesRepository.UpsertAsync(payAttributes);

            var employee = await _repository.GetByIdAsync(employeeId);
            if (employee != null)
            {
                employee.PayAttributes = payAttributes;
                await _subscriptionPublisher.PublishPayAttributesChangeAsync(employee);
            }
        }
    }
}

public class EmployeeEventPayload
{
    // Direct event fields (used when Dapr outbox projection works correctly)
    public Guid EventId { get; set; }
    public DateTime OccurredOn { get; set; }
    public string EventType { get; set; } = string.Empty;
    public Guid EmployeeId { get; set; }

    // Entity fields (always present in the Dapr outbox entity state format)
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int? PayType { get; set; }
    public decimal? PayRate { get; set; }
    public decimal? PayPeriodHours { get; set; }
    public bool IsActive { get; set; } = true;

    // Nested domain events from Dapr outbox entity state (Dapr bug #8130:
    // outbox publishes entity state instead of event projection)
    public List<DomainEventInfo>? DomainEvents { get; set; }

    /// <summary>
    /// Resolves the effective EmployeeId, EventId, EventType, and OccurredOn
    /// regardless of whether the payload is the event projection or entity state.
    /// </summary>
    public (Guid EmployeeId, Guid EventId, string EventType, DateTime OccurredOn) ResolveEventInfo()
    {
        // If direct event fields are populated, use them
        if (EventId != Guid.Empty && !string.IsNullOrEmpty(EventType))
            return (EmployeeId, EventId, EventType, OccurredOn);

        // Otherwise extract from nested DomainEvents (entity state format)
        var domainEvent = DomainEvents?.FirstOrDefault();
        if (domainEvent != null)
            return (Id, domainEvent.EventId, domainEvent.EventType, domainEvent.OccurredOn);

        return (Id, Guid.Empty, string.Empty, DateTime.UtcNow);
    }
}

public class DomainEventInfo
{
    public Guid EventId { get; set; }
    public DateTime OccurredOn { get; set; }
    public string EventType { get; set; } = string.Empty;
    public decimal? CurrentBalance { get; set; }
    public decimal? OriginalAmount { get; set; }
}

public class TransferEventPayload
{
    // Transfer entity fields
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public decimal Amount { get; set; }
    public long PayPeriodNumber { get; set; }
    public string? ExternalReferenceId { get; set; }
    public string? FailureReason { get; set; }
    public decimal? CurrentBalance { get; set; }
    public string? Status { get; set; }
    public List<WorkflowStepPayload>? WorkflowSteps { get; set; }

    // Nested domain events
    public List<DomainEventInfo>? DomainEvents { get; set; }

    public string? SerializeWorkflowSteps()
    {
        if (WorkflowSteps == null || WorkflowSteps.Count == 0)
            return null;
        return JsonSerializer.Serialize(WorkflowSteps, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    public (Guid TransferId, Guid EmployeeId, string EventType) ResolveTransferInfo()
    {
        // Prefer the entity Status field (reliable), fall back to domain event type
        if (!string.IsNullOrEmpty(Status))
        {
            var eventType = Status switch
            {
                "Initiated" => "transfer.initiated",
                "Processing" => "transfer.processing",
                "Completed" => "transfer.completed",
                "Failed" => "transfer.failed",
                "AwaitingConfirmation" => "transfer.balance_changed",
                _ => DomainEvents?.LastOrDefault()?.EventType ?? string.Empty
            };
            return (Id, EmployeeId, eventType);
        }

        var domainEvent = DomainEvents?.LastOrDefault();
        var evtType = domainEvent?.EventType ?? string.Empty;
        return (Id, EmployeeId, evtType);
    }

    public decimal? ResolveCurrentBalance()
    {
        return CurrentBalance ?? DomainEvents?.FirstOrDefault(e => e.EventType == "transfer.balance_changed")?.CurrentBalance;
    }
}

public class NetPayEventPayload
{
    [JsonPropertyName("EMPLOYEE_ID")]
    public string EmployeeId { get; set; } = string.Empty;

    [JsonPropertyName("PAY_PERIOD_NUMBER")]
    public long PayPeriodNumber { get; set; }

    [JsonPropertyName("GROSS_PAY")]
    public double GrossPay { get; set; }

    [JsonPropertyName("FEDERAL_TAX")]
    public double FederalTax { get; set; }

    [JsonPropertyName("STATE_TAX")]
    public double StateTax { get; set; }

    [JsonPropertyName("ADDITIONAL_FEDERAL_WITHHOLDING")]
    public double AdditionalFederalWithholding { get; set; }

    [JsonPropertyName("ADDITIONAL_STATE_WITHHOLDING")]
    public double AdditionalStateWithholding { get; set; }

    [JsonPropertyName("TOTAL_TAX")]
    public double TotalTax { get; set; }

    [JsonPropertyName("TOTAL_FIXED_DEDUCTIONS")]
    public double TotalFixedDeductions { get; set; }

    [JsonPropertyName("TOTAL_PERCENT_DEDUCTIONS")]
    public double TotalPercentDeductions { get; set; }

    [JsonPropertyName("TOTAL_DEDUCTIONS")]
    public double TotalDeductions { get; set; }

    [JsonPropertyName("NET_PAY")]
    public double NetPay { get; set; }

    [JsonPropertyName("PAY_RATE")]
    public double PayRate { get; set; }

    [JsonPropertyName("PAY_TYPE")]
    public string? PayType { get; set; }

    [JsonPropertyName("TOTAL_HOURS_WORKED")]
    public double TotalHoursWorked { get; set; }

    [JsonPropertyName("PAY_PERIOD_START")]
    public string? PayPeriodStart { get; set; }

    [JsonPropertyName("PAY_PERIOD_END")]
    public string? PayPeriodEnd { get; set; }
}

public class WorkflowStepPayload
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Detail { get; set; }
    public int RetryCount { get; set; }
}
