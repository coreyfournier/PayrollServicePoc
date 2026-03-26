using PayrollService.Application.Interfaces;
using PayrollService.Domain.Common;

namespace PayrollService.DatabaseTests.TestDoubles;

public class TestUnitOfWork : IUnitOfWork
{
    private readonly List<DomainEvent> _publishedEvents = new();
    public IReadOnlyList<DomainEvent> PublishedEvents => _publishedEvents.AsReadOnly();

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        var result = await operation();
        _publishedEvents.AddRange(entity.DomainEvents);
        entity.ClearDomainEvents();
        return result;
    }

    public async Task ExecuteAsync(Func<Task> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        await operation();
        _publishedEvents.AddRange(entity.DomainEvents);
        entity.ClearDomainEvents();
    }

    public void Clear() => _publishedEvents.Clear();
}
