using System.Text;
using Confluent.Kafka;

namespace ListenerApi.Consumers;

/// <summary>
/// Wrapper for raw Kafka message values consumed from topics not produced by MassTransit
/// (e.g., Dapr outbox CloudEvents, Java NetPayProcessor).
/// Each topic gets its own message type so MassTransit can route to the correct consumer.
/// </summary>
public class EmployeeEventMessage
{
    public string Value { get; set; } = string.Empty;
}

public class TransferEventMessage
{
    public string Value { get; set; } = string.Empty;
}

public class NetPayEventMessage
{
    public string Value { get; set; } = string.Empty;
}

public class TransferLimitsMessage
{
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Confluent.Kafka deserializer that reads raw bytes as a UTF-8 string
/// and wraps them in a typed message for MassTransit routing.
/// </summary>
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
