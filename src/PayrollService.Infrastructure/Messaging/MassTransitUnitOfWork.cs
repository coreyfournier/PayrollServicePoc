using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Common;

namespace PayrollService.Infrastructure.Messaging;

public class MassTransitUnitOfWork : IUnitOfWork
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<MassTransitUnitOfWork> _logger;

    public MassTransitUnitOfWork(IProducer<string, string> producer, ILogger<MassTransitUnitOfWork> logger)
    {
        _producer = producer;
        _logger = logger;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        var domainEvents = entity.DomainEvents.ToList();

        // Step 1: Execute the repository operation (MongoDB is now the sole source of truth)
        var result = await operation();

        // Step 2: Publish domain events to Kafka
        if (domainEvents.Count > 0)
        {
            await PublishEvents(entity, cancellationToken);
        }

        entity.ClearDomainEvents();
        return result;
    }

    public async Task ExecuteAsync(Func<Task> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        var domainEvents = entity.DomainEvents.ToList();

        // Step 1: Execute the repository operation (MongoDB is now the sole source of truth)
        await operation();

        // Step 2: Publish domain events to Kafka
        if (domainEvents.Count > 0)
        {
            await PublishEvents(entity, cancellationToken);
        }

        entity.ClearDomainEvents();
    }

    private async Task PublishEvents(Entity entity, CancellationToken cancellationToken)
    {
        var topicName = CloudEventWrapper.GetTopicName(entity);
        var cloudEventJson = CloudEventWrapper.Wrap(entity);

        try
        {
            var message = new Message<string, string>
            {
                Key = entity.Id.ToString(),
                Value = cloudEventJson
            };

            var result = await _producer.ProduceAsync(topicName, message, cancellationToken);

            _logger.LogDebug("Published CloudEvent to {Topic} partition {Partition} offset {Offset} for {EntityType} {EntityId}",
                topicName, result.Partition.Value, result.Offset.Value, entity.GetType().Name, entity.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish events to {Topic} for {EntityType} {EntityId}",
                topicName, entity.GetType().Name, entity.Id);
            throw;
        }
    }
}
