using System.Text.Json;
using Dapr;
using ListenerApi.Data.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ListenerApi.Controllers;

[AllowAnonymous]
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
    public async Task<IActionResult> HandleEmployeeEvent()
    {
        // Dapr outbox stringifies the JSON data field (Dapr bug #8130), so
        // UseCloudEvents() can't properly unwrap it. Read the original body
        // captured by EnableBuffering middleware and extract "data" manually.
        var body = HttpContext.Items["RawBody"] as string ?? string.Empty;

        _logger.LogInformation("Received employee event, body length={Length}", body.Length);

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            EmployeeEventPayload? eventData = null;
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                if (dataElement.ValueKind == JsonValueKind.String)
                {
                    // data is stringified JSON (Dapr bug #8130) — parse the string
                    var dataString = dataElement.GetString();
                    if (!string.IsNullOrEmpty(dataString))
                        eventData = JsonSerializer.Deserialize<EmployeeEventPayload>(dataString, options);
                }
                else
                {
                    // data is a proper JSON object
                    eventData = dataElement.Deserialize<EmployeeEventPayload>(options);
                }
            }
            else
            {
                // No CloudEvent wrapper — try direct deserialization
                eventData = doc.RootElement.Deserialize<EmployeeEventPayload>(options);
            }

            if (eventData == null)
            {
                _logger.LogWarning("Failed to deserialize employee event");
                return BadRequest("Invalid payload");
            }

            var (employeeId, eventId, eventType, _) = eventData.ResolveEventInfo();
            _logger.LogInformation("Processing employee event: {EventType} {EventId} for {EmployeeId}",
                eventType, eventId, employeeId);

            await _eventProcessor.ProcessEmployeeEventAsync(eventData);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing employee event, body={Body}", body);
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
                if (dataElement.ValueKind == JsonValueKind.String)
                {
                    var dataString = dataElement.GetString();
                    if (!string.IsNullOrEmpty(dataString))
                        eventData = JsonSerializer.Deserialize<NetPayEventPayload>(dataString, options);
                }
                else
                {
                    eventData = dataElement.Deserialize<NetPayEventPayload>(options);
                }
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
