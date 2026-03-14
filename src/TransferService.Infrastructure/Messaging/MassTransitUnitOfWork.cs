using Microsoft.Extensions.Logging;
using TransferService.Application.Interfaces;
using TransferService.Domain.Common;
using TransferService.Domain.Entities;

namespace TransferService.Infrastructure.Messaging;

public class MassTransitUnitOfWork : IUnitOfWork
{
    private readonly ITransferEventPublisher _eventPublisher;
    private readonly ILogger<MassTransitUnitOfWork> _logger;

    public MassTransitUnitOfWork(
        ITransferEventPublisher eventPublisher,
        ILogger<MassTransitUnitOfWork> logger)
    {
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        var result = await operation();

        if (entity is Transfer transfer && entity.DomainEvents.Count > 0)
        {
            await _eventPublisher.PublishAsync(transfer, cancellationToken);
        }

        entity.ClearDomainEvents();
        return result;
    }

    public async Task ExecuteAsync(Func<Task> operation, Entity entity, CancellationToken cancellationToken = default)
    {
        await operation();

        if (entity is Transfer transfer && entity.DomainEvents.Count > 0)
        {
            await _eventPublisher.PublishAsync(transfer, cancellationToken);
        }

        entity.ClearDomainEvents();
    }
}
