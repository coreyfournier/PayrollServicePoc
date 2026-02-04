using System.Text.Json;
using MongoDB.Driver;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Common;
using PayrollService.Infrastructure.Persistence;

namespace PayrollService.Infrastructure.Events;

public class TransactionalUnitOfWork : IUnitOfWork
{
    private readonly MongoDbContext _context;
    private readonly IEventPublisher _eventPublisher;

    public TransactionalUnitOfWork(MongoDbContext context, IEventPublisher eventPublisher)
    {
        _context = context;
        _eventPublisher = eventPublisher;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        // Store events to outbox before the operation
        var domainEvents = entity.DomainEvents.ToList();

        // Execute the database operation
        var result = await operation();

        // Save events to outbox for guaranteed delivery
        foreach (var domainEvent in domainEvents)
        {
            var outboxMessage = new OutboxMessage
            {
                EventType = domainEvent.EventType,
                EventData = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                CreatedAt = DateTime.UtcNow
            };
            await _context.OutboxMessages.InsertOneAsync(outboxMessage, cancellationToken: cancellationToken);
        }

        // Publish events via Dapr
        await _eventPublisher.PublishAsync(domainEvents, cancellationToken);

        // Mark outbox messages as processed
        foreach (var domainEvent in domainEvents)
        {
            await _context.OutboxMessages.UpdateOneAsync(
                o => o.EventType == domainEvent.EventType && o.ProcessedAt == null,
                Builders<OutboxMessage>.Update.Set(o => o.ProcessedAt, DateTime.UtcNow),
                cancellationToken: cancellationToken);
        }

        entity.ClearDomainEvents();
        return result;
    }

    public async Task ExecuteAsync(Func<Task> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async () =>
        {
            await operation();
            return true;
        }, entity, cancellationToken);
    }
}
