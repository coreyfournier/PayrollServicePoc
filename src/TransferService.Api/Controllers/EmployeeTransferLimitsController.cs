using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using TransferService.Application.Commands.TransferLimits;
using TransferService.Application.DTOs;
using TransferService.Application.Messages;
using TransferService.Application.Queries.TransferLimits;

namespace TransferService.Api.Controllers;

[ApiController]
[Route("api/transfers/employee/{employeeId:guid}/custom-limits")]
public class EmployeeTransferLimitsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IPublishEndpoint _publishEndpoint;

    public EmployeeTransferLimitsController(IMediator mediator, IPublishEndpoint publishEndpoint)
    {
        _mediator = mediator;
        _publishEndpoint = publishEndpoint;
    }

    [HttpGet]
    public async Task<ActionResult<EmployeeTransferLimitsDto>> Get(Guid employeeId)
    {
        var result = await _mediator.Send(new GetEmployeeTransferLimitsQuery(employeeId));
        if (result == null)
            return NotFound();
        return Ok(result);
    }

    [HttpPut]
    public async Task<ActionResult<EmployeeTransferLimitsDto>> Set(Guid employeeId, [FromBody] SetEmployeeTransferLimitsDto dto)
    {
        var result = await _mediator.Send(new SetEmployeeTransferLimitsCommand(
            employeeId,
            dto.MaxTransfersPerPayPeriod,
            dto.MaxAmountPerPayPeriod,
            dto.MaxTransfersPerDay));
        await _publishEndpoint.Publish(new EmployeeLimitsUpdated(employeeId));
        return Ok(result);
    }

    [HttpDelete]
    public async Task<ActionResult> Delete(Guid employeeId)
    {
        await _mediator.Send(new DeleteEmployeeTransferLimitsCommand(employeeId));
        await _publishEndpoint.Publish(new EmployeeLimitsUpdated(employeeId));
        return NoContent();
    }
}

public record SetEmployeeTransferLimitsDto(
    int MaxTransfersPerPayPeriod,
    decimal MaxAmountPerPayPeriod,
    int MaxTransfersPerDay);
