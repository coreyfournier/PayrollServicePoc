using Dapr;
using Microsoft.AspNetCore.Mvc;

namespace PayrollService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventSubscriptionController : ControllerBase
{
    private readonly ILogger<EventSubscriptionController> _logger;

    public EventSubscriptionController(ILogger<EventSubscriptionController> logger)
    {
        _logger = logger;
    }

    [Topic("kafka-pubsub", "employee-events")]
    [HttpPost("employee-events")]
    public IActionResult HandleEmployeeEvent([FromBody] object eventData)
    {
        _logger.LogInformation("Received employee event: {EventData}", eventData);
        return Ok();
    }

    [Topic("kafka-pubsub", "timeentry-events")]
    [HttpPost("timeentry-events")]
    public IActionResult HandleTimeEntryEvent([FromBody] object eventData)
    {
        _logger.LogInformation("Received time entry event: {EventData}", eventData);
        return Ok();
    }

    [Topic("kafka-pubsub", "taxinfo-events")]
    [HttpPost("taxinfo-events")]
    public IActionResult HandleTaxInfoEvent([FromBody] object eventData)
    {
        _logger.LogInformation("Received tax info event: {EventData}", eventData);
        return Ok();
    }

    [Topic("kafka-pubsub", "deduction-events")]
    [HttpPost("deduction-events")]
    public IActionResult HandleDeductionEvent([FromBody] object eventData)
    {
        _logger.LogInformation("Received deduction event: {EventData}", eventData);
        return Ok();
    }
}
