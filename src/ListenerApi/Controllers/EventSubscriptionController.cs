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
        _logger.LogInformation("Received employee event: {EventType} {EventId}",
            eventData.EventType, eventData.EventId);

        try
        {
            await _eventProcessor.ProcessEmployeeEventAsync(eventData);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing employee event {EventId}", eventData.EventId);
            return StatusCode(500, "Error processing event");
        }
    }
}
