using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayrollService.Application.Commands.TimeEntry;
using PayrollService.Application.DTOs;
using PayrollService.Application.Queries.TimeEntry;

namespace PayrollService.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class TimeEntriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public TimeEntriesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("employee/{employeeId:guid}")]
    public async Task<ActionResult<IEnumerable<TimeEntryDto>>> GetByEmployee(Guid employeeId)
    {
        var result = await _mediator.Send(new GetTimeEntriesByEmployeeQuery(employeeId));
        return Ok(result);
    }

    [HttpPost("clock-in/{employeeId:guid}")]
    public async Task<ActionResult<TimeEntryDto>> ClockIn(Guid employeeId)
    {
        var result = await _mediator.Send(new ClockInCommand(employeeId));
        return Ok(result);
    }

    [HttpPost("clock-out/{employeeId:guid}")]
    public async Task<ActionResult<TimeEntryDto>> ClockOut(Guid employeeId)
    {
        var result = await _mediator.Send(new ClockOutCommand(employeeId));
        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TimeEntryDto>> Update(Guid id, [FromBody] UpdateTimeEntryCommand command)
    {
        var result = await _mediator.Send(command with { Id = id });
        return Ok(result);
    }
}
