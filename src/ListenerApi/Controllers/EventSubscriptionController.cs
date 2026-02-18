using System.Text.Json;
using Dapr;
using ListenerApi.Data.Services;
using Microsoft.AspNetCore.Mvc;

namespace ListenerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventSubscriptionController : ControllerBase
{
    private readonly ILogger<EventSubscriptionController> _logger;
    private readonly EventProcessor _eventProcessor;

    public EventSubscriptionController(
        ILogger<EventSubscriptionController> logger,
        EventProcessor eventProcessor)
    {
        _logger = logger;
        _eventProcessor = eventProcessor;
    }

    [Dapr.Topic("kafka-pubsub-listener", "employee-events")]
    [HttpPost("employee-events")]
    public async Task<IActionResult> HandleEmployeeEvent([FromBody] EmployeeEventPayload eventData)
    {
        var (employeeId, eventId, eventType, _) = eventData.ResolveEventInfo();
        _logger.LogInformation("Received employee event: {EventType} {EventId} for {EmployeeId}",
            eventType, eventId, employeeId);

        try
        {
            await _eventProcessor.ProcessEmployeeEventAsync(eventData);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing employee event {EventId}", eventId);
            return StatusCode(500, "Error processing event");
        }
    }

    [Dapr.Topic("kafka-pubsub-listener", "employee-net-pay")]
    [HttpPost("employee-net-pay")]
    public async Task<IActionResult> HandleNetPayEvent()
    {
        // Raw Kafka messages (from NetPayProcessor) arrive wrapped in a CloudEvent by Dapr.
        // The UseCloudEvents() middleware can't unwrap them properly (empty body), so we
        // read the original body captured by EnableBuffering middleware and extract "data".
        var body = HttpContext.Items["RawBody"] as string ?? string.Empty;

        _logger.LogInformation("Received net pay event, body length={Length}", body.Length);

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // The body might be a CloudEvent with a "data" field, or raw JSON
            NetPayEventPayload? eventData = null;
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                eventData = dataElement.Deserialize<NetPayEventPayload>(options);
            }
            else
            {
                eventData = doc.RootElement.Deserialize<NetPayEventPayload>(options);
            }

            if (eventData == null)
            {
                _logger.LogWarning("Failed to deserialize net pay event");
                return BadRequest("Invalid payload");
            }

            await _eventProcessor.ProcessNetPayEventAsync(eventData);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing net pay event, body={Body}", body);
            return StatusCode(500, "Error processing event");
        }
    }
}
