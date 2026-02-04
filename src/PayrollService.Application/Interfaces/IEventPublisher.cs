using PayrollService.Domain.Common;

namespace PayrollService.Application.Interfaces;

public interface IEventPublisher
{
    Task PublishAsync(DomainEvent domainEvent, CancellationToken cancellationToken = default);
    Task PublishAsync(IEnumerable<DomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
