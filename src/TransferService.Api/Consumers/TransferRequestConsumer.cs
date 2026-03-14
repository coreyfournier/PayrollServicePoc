using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using TransferService.Application.Messages;

namespace TransferService.Api.Consumers;

public record TransferRequestMessage
{
    public string? Action { get; init; }
    public Guid EmployeeId { get; init; }
    public decimal Amount { get; init; }
    public long PayPeriodNumber { get; init; }
    public Guid BankAccountId { get; init; }
    public Guid? TransferId { get; init; }
    public bool Accepted { get; init; }
}

public class TransferRequestConsumer : IConsumer<TransferRequestMessage>
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<TransferRequestConsumer> _logger;

    public TransferRequestConsumer(
        IPublishEndpoint publishEndpoint,
        ILogger<TransferRequestConsumer> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TransferRequestMessage> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received transfer request: Action={Action}, EmployeeId={EmployeeId}, Amount={Amount}",
            message.Action, message.EmployeeId, message.Amount);

        if (string.Equals(message.Action, "accept-balance", StringComparison.OrdinalIgnoreCase))
        {
            if (message.TransferId == null || message.TransferId == Guid.Empty)
            {
                _logger.LogWarning("Accept-balance message missing TransferId");
                return;
            }

            await _publishEndpoint.Publish(new AcceptBalanceMessage(
                message.TransferId.Value, message.EmployeeId, message.Accepted));
        }
        else
        {
            var transferId = message.TransferId ?? Guid.NewGuid();

            await _publishEndpoint.Publish(new InitiateTransferMessage(
                transferId, message.EmployeeId, message.Amount, message.PayPeriodNumber, message.BankAccountId));
        }
    }
}
