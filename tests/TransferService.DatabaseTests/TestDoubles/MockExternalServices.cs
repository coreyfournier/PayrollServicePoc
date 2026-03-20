using TransferService.Application.Interfaces;
using TransferService.Domain.Entities;

namespace TransferService.DatabaseTests.TestDoubles;

public class TestUnitOfWork : IUnitOfWork
{
    private readonly List<TransferService.Domain.Common.DomainEvent> _publishedEvents = new();
    public IReadOnlyList<TransferService.Domain.Common.DomainEvent> PublishedEvents => _publishedEvents.AsReadOnly();

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, TransferService.Domain.Common.Entity entity, CancellationToken cancellationToken = default)
    {
        var result = await operation();
        _publishedEvents.AddRange(entity.DomainEvents);
        entity.ClearDomainEvents();
        return result;
    }

    public async Task ExecuteAsync(Func<Task> operation, TransferService.Domain.Common.Entity entity, CancellationToken cancellationToken = default)
    {
        await operation();
        _publishedEvents.AddRange(entity.DomainEvents);
        entity.ClearDomainEvents();
    }

    public void Clear() => _publishedEvents.Clear();
}
