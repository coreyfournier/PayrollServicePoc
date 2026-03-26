// tests/KafkaPipeline.Tests/Helpers/CloudEventProducer.cs
using System.Text.Json;
using System.Text.Json.Nodes;
using Confluent.Kafka;

namespace KafkaPipeline.Tests.Helpers;

public class CloudEventProducer : IDisposable
{
    private readonly IProducer<string, string> _producer;

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public CloudEventProducer(string bootstrapServers)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            ClientId = "kafka-pipeline-test"
        };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task ProduceAsync(string topic, string key, object entity)
    {
        var entityNode = JsonSerializer.SerializeToNode(entity, SerializeOptions);
        var cloudEvent = new JsonObject
        {
            ["type"] = "com.dapr.event.sent",
            ["source"] = "payroll-api",
            ["data"] = entityNode
        };

        var message = new Message<string, string>
        {
            Key = key,
            Value = cloudEvent.ToJsonString(SerializeOptions)
        };

        await _producer.ProduceAsync(topic, message);
    }

    public void Dispose() => _producer?.Dispose();
}
