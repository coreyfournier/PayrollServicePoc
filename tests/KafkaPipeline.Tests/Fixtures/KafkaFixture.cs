// tests/KafkaPipeline.Tests/Fixtures/KafkaFixture.cs
namespace KafkaPipeline.Tests.Fixtures;

public class KafkaFixture : IAsyncLifetime
{
    public string BootstrapServers => Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") ?? "localhost:29092";
    public string KsqlDbUrl => Environment.GetEnvironmentVariable("KSQLDB_URL") ?? "http://localhost:8088";

    public async Task InitializeAsync()
    {
        // Wait for Kafka to be ready
        using var adminClient = new Confluent.Kafka.AdminClientBuilder(
            new Confluent.Kafka.AdminClientConfig { BootstrapServers = BootstrapServers })
            .Build();

        var retries = 30;
        while (retries-- > 0)
        {
            try
            {
                var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(5));
                if (metadata.Brokers.Count > 0)
                    return;
            }
            catch
            {
                // Not ready yet
            }
            await Task.Delay(2000);
        }
        throw new Exception("Kafka did not become ready within timeout");
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition("KafkaPipeline")]
public class KafkaPipelineCollection : ICollectionFixture<KafkaFixture> { }
