using ListenerApi.Data.Entities;

namespace ListenerApi.Data.Services;

public interface ISubscriptionPublisher
{
    Task PublishEmployeeChangeAsync(EmployeeRecord employee, string eventType);
    Task PublishPayAttributesChangeAsync(EmployeeRecord employee);
    Task PublishTransferChangeAsync(TransferRecord transfer, string eventType);
}
