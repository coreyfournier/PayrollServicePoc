using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using MassTransit;
using Microsoft.Extensions.Logging;
using TransferService.Application.Messages;

namespace TransferService.Api.Consumers;

public class TransferRequestMessage
{
    public string Value { get; set; } = string.Empty;
}

public class RawStringDeserializer<T> : IDeserializer<T>
    where T : new()
{
    private readonly Action<T, string> _valueSetter;

    public RawStringDeserializer(Action<T, string> valueSetter)
    {
        _valueSetter = valueSetter;
    }

    public T Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
    {
        var msg = new T();
        if (!isNull && data.Length > 0)
        {
            _valueSetter(msg, Encoding.UTF8.GetString(data));
        }
        return msg;
    }
}

public record TransferRequestPayload
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
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TransferRequestConsumer(
        IPublishEndpoint publishEndpoint,
        ILogger<TransferRequestConsumer> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TransferRequestMessage> context)
    {
        var raw = context.Message.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            _logger.LogWarning("Received empty transfer request message");
            return;
        }

        _logger.LogInformation("Received transfer request: {Raw}", raw);

        var message = JsonSerializer.Deserialize<TransferRequestPayload>(raw, _jsonOptions);
        if (message == null)
        {
            _logger.LogWarning("Failed to deserialize transfer request: {Raw}", raw);
            return;
        }

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
