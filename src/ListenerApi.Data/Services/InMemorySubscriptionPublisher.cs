using HotChocolate.Subscriptions;
using ListenerApi.Data.Entities;
using Microsoft.Extensions.Logging;

namespace ListenerApi.Data.Services;

public class InMemorySubscriptionPublisher : ISubscriptionPublisher
{
    private readonly ITopicEventSender _eventSender;
    private readonly ILogger<InMemorySubscriptionPublisher> _logger;

    public InMemorySubscriptionPublisher(
        ITopicEventSender eventSender,
        ILogger<InMemorySubscriptionPublisher> logger)
    {
        _eventSender = eventSender;
        _logger = logger;
    }

    public async Task PublishEmployeeChangeAsync(EmployeeRecord employee, string eventType)
    {
        try
        {
            var change = new EmployeeChange
            {
                Employee = employee,
                ChangeType = eventType.Contains('.') ? eventType.Split('.')[1] : eventType,
                Timestamp = DateTime.UtcNow
            };

            await _eventSender.SendAsync("EmployeeChanges", change);
            _logger.LogInformation("Published employee change: {ChangeType} for {EmployeeId}",
                change.ChangeType, employee.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish employee change for {EmployeeId}", employee.Id);
            throw;
        }
    }
}
