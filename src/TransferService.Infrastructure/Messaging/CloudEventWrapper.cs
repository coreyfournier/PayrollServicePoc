using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransferService.Infrastructure.Messaging;

public class CloudEventWrapper
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("source")]
    public string Source { get; set; } = "transfer-api";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "com.dapr.event.sent";

    [JsonPropertyName("specversion")]
    public string SpecVersion { get; set; } = "1.0";

    [JsonPropertyName("datacontenttype")]
    public string DataContentType { get; set; } = "application/json";

    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public string Time { get; set; } = DateTime.UtcNow.ToString("O");

    [JsonPropertyName("traceid")]
    public string TraceId { get; set; } = string.Empty;

    public static CloudEventWrapper Create(string data)
    {
        return new CloudEventWrapper
        {
            Data = data
        };
    }
}
