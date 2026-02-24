using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayrollService.Application.Commands.Deduction;
using PayrollService.Application.DTOs;
using PayrollService.Application.Queries.Deduction;

namespace PayrollService.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DeductionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public DeductionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("employee/{employeeId:guid}")]
    public async Task<ActionResult<IEnumerable<DeductionDto>>> GetByEmployee(Guid employeeId)
    {
        var result = await _mediator.Send(new GetDeductionsByEmployeeQuery(employeeId));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<DeductionDto>> Create([FromBody] CreateDeductionDto dto)
    {
        var command = new CreateDeductionCommand(
            dto.EmployeeId,
            dto.DeductionType,
            dto.Description,
            dto.Amount,
            dto.IsPercentage);

        var result = await _mediator.Send(command);
        return Created($"/api/deductions/{result.Id}", result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DeductionDto>> Update(Guid id, [FromBody] UpdateDeductionDto dto)
    {
        var command = new UpdateDeductionCommand(
            id,
            dto.DeductionType,
            dto.Description,
            dto.Amount,
            dto.IsPercentage);

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id)
    {
        await _mediator.Send(new DeleteDeductionCommand(id));
        return NoContent();
    }
}
