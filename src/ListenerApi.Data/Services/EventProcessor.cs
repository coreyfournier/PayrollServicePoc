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
        var existing = await _repository.GetByIdAsync(eventData.EmployeeId);

        // Idempotency checks
        if (existing != null)
        {
            if (existing.LastEventId == eventData.EventId)
            {
                _logger.LogInformation("Skipping duplicate event {EventId}", eventData.EventId);
                return;
            }

            if (existing.LastEventTimestamp >= eventData.OccurredOn)
            {
                _logger.LogInformation("Skipping older event {EventId} - existing timestamp {ExistingTimestamp} >= incoming {IncomingTimestamp}",
                    eventData.EventId, existing.LastEventTimestamp, eventData.OccurredOn);
                return;
            }
        }

        // Process based on event type
        var record = existing ?? new EmployeeRecord { Id = eventData.EmployeeId };

        switch (eventData.EventType)
        {
            case "employee.created":
            case "employee.updated":
                record.FirstName = eventData.FirstName;
                record.LastName = eventData.LastName;
                record.Email = eventData.Email;
                record.PayType = eventData.PayType?.ToString() ?? string.Empty;
                record.PayRate = eventData.PayRate;
                record.IsActive = true;
                break;
            case "employee.deactivated":
                record.IsActive = false;
                break;
            case "employee.activated":
                record.IsActive = true;
                break;
            default:
                _logger.LogWarning("Unknown event type {EventType}", eventData.EventType);
                return;
        }

        // Update tracking
        record.LastEventType = eventData.EventType;
        record.LastEventTimestamp = eventData.OccurredOn;
        record.LastEventId = eventData.EventId;
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
        await _subscriptionPublisher.PublishEmployeeChangeAsync(record, eventData.EventType);
    }
}

public class EmployeeEventPayload
{
    public Guid EventId { get; set; }
    public DateTime OccurredOn { get; set; }
    public string EventType { get; set; } = string.Empty;
    public Guid EmployeeId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int? PayType { get; set; }
    public decimal? PayRate { get; set; }
}
