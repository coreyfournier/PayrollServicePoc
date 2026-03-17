namespace TransferService.Application.Interfaces;

public interface ILimitsEventPublisher
{
    Task PublishAsync(Guid employeeId, int maxPerPayPeriod, decimal maxAmountPerPayPeriod, int maxPerDay, CancellationToken cancellationToken = default);
}
