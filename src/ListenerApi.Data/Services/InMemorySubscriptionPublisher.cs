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

    public async Task PublishPayAttributesChangeAsync(EmployeeRecord employee)
    {
        try
        {
            var change = new EmployeeChange
            {
                Employee = employee,
                ChangeType = "payUpdated",
                Timestamp = DateTime.UtcNow
            };

            await _eventSender.SendAsync("EmployeeChanges", change);
            _logger.LogInformation("Published pay attributes change for {EmployeeId}", employee.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish pay attributes change for {EmployeeId}", employee.Id);
            throw;
        }
    }

    public async Task PublishTransferChangeAsync(TransferRecord transfer, string eventType)
    {
        try
        {
            var change = new TransferChange
            {
                Transfer = transfer,
                ChangeType = eventType.Contains('.') ? eventType.Split('.')[1] : eventType,
                Timestamp = DateTime.UtcNow
            };

            await _eventSender.SendAsync("TransferChanges", change);
            _logger.LogInformation("Published transfer change: {ChangeType} for {TransferId}",
                change.ChangeType, transfer.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish transfer change for {TransferId}", transfer.Id);
            throw;
        }
    }

    public async Task PublishTransferStatusChangeAsync(EmployeeTransferStatus status)
    {
        try
        {
            var change = new TransferStatusChange
            {
                TransferStatus = status,
                Timestamp = DateTime.UtcNow
            };

            await _eventSender.SendAsync("TransferStatusChanges", change);
            _logger.LogInformation("Published transfer status change for employee {EmployeeId}, canTransfer={CanTransfer}",
                status.EmployeeId, status.CanTransfer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish transfer status change for employee {EmployeeId}", status.EmployeeId);
            throw;
        }
    }
}
