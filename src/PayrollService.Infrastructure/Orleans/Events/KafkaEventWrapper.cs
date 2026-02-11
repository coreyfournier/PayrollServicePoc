namespace PayrollService.Infrastructure.Orleans.Events;

[GenerateSerializer]
public class KafkaEventWrapper
{
    [Id(0)]
    public Guid EventId { get; set; }

    [Id(1)]
    public DateTime OccurredOn { get; set; }

    [Id(2)]
    public string EventType { get; set; } = string.Empty;

    [Id(3)]
    public string Payload { get; set; } = string.Empty;
}
