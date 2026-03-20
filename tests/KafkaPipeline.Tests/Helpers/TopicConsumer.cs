// tests/KafkaPipeline.Tests/Helpers/TopicConsumer.cs
using System.Text.Json;
using Confluent.Kafka;

namespace KafkaPipeline.Tests.Helpers;

public class TopicConsumer : IDisposable
{
    private readonly IConsumer<string, string> _consumer;

    public TopicConsumer(string bootstrapServers, string groupId)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };
        _consumer = new ConsumerBuilder<string, string>(config).Build();
    }

    public void Subscribe(string topic) => _consumer.Subscribe(topic);

    public async Task<List<(string Key, JsonDocument Value)>> ConsumeUntilAsync(
        Func<List<(string Key, JsonDocument Value)>, bool> predicate,
        TimeSpan timeout)
    {
        var results = new List<(string Key, JsonDocument Value)>();
        var cts = new CancellationTokenSource(timeout);

        while (!cts.IsCancellationRequested)
        {
            try
            {
                var result = _consumer.Consume(TimeSpan.FromSeconds(1));
                if (result?.Message?.Value != null)
                {
                    var doc = JsonDocument.Parse(result.Message.Value);
                    results.Add((result.Message.Key, doc));
                    if (predicate(results))
                        return results;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return results;
    }

    public void Dispose() => _consumer?.Dispose();
}
