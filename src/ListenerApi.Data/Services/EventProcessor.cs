using ListenerApi.Data.Entities;
using ListenerApi.Data.Repositories;
using Microsoft.Extensions.Logging;

namespace ListenerApi.Data.Services;

public class EventProcessor
{
    private readonly IEmployeeRecordRepository _repository;
    private readonly ISubscriptionPublisher _subscriptionPublisher;
    private readonly ILogger<EventProcessor> _logger;

    public EventProcessor(
        IEmployeeRecordRepository repository,
        ISubscriptionPublisher subscriptionPublisher,
        ILogger<EventProcessor> logger)
    {
        _repository = repository;
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
                record.IsActive = eventData.IsActive;
                break;
            case "employee.deactivated":
                record.IsActive = false;
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
}
