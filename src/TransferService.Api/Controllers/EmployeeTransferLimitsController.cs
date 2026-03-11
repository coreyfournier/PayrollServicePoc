using MediatR;
using Microsoft.AspNetCore.Mvc;
using TransferService.Application.Commands.TransferLimits;
using TransferService.Application.DTOs;
using TransferService.Application.Queries.TransferLimits;

namespace TransferService.Api.Controllers;

[ApiController]
[Route("api/transfers/employee/{employeeId:guid}/custom-limits")]
public class EmployeeTransferLimitsController : ControllerBase
{
    private readonly IMediator _mediator;

    public EmployeeTransferLimitsController(IMediator mediator)
    {
        _mediator = mediator;
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
        return Ok(result);
    }

    [HttpDelete]
    public async Task<ActionResult> Delete(Guid employeeId)
    {
        await _mediator.Send(new DeleteEmployeeTransferLimitsCommand(employeeId));
        return NoContent();
    }
}

public record SetEmployeeTransferLimitsDto(
    int MaxTransfersPerPayPeriod,
    decimal MaxAmountPerPayPeriod,
    int MaxTransfersPerDay);
