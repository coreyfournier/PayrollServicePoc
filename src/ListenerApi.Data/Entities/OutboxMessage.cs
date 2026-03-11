namespace ListenerApi.Data.Entities;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public string AggregateId { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
