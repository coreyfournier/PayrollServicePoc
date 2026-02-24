using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayrollService.Application.Commands.TaxInformation;
using PayrollService.Application.DTOs;
using PayrollService.Application.Queries.TaxInformation;

namespace PayrollService.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class TaxInformationController : ControllerBase
{
    private readonly IMediator _mediator;

    public TaxInformationController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("employee/{employeeId:guid}")]
    public async Task<ActionResult<TaxInformationDto>> GetByEmployee(Guid employeeId)
    {
        var result = await _mediator.Send(new GetTaxInformationByEmployeeQuery(employeeId));
        if (result == null)
            return NotFound();
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<TaxInformationDto>> Create([FromBody] CreateTaxInformationDto dto)
    {
        var command = new CreateTaxInformationCommand(
            dto.EmployeeId,
            dto.FederalFilingStatus,
            dto.FederalAllowances,
            dto.AdditionalFederalWithholding,
            dto.State,
            dto.StateFilingStatus,
            dto.StateAllowances,
            dto.AdditionalStateWithholding);

        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetByEmployee), new { employeeId = result.EmployeeId }, result);
    }

    [HttpPut("employee/{employeeId:guid}")]
    public async Task<ActionResult<TaxInformationDto>> Update(Guid employeeId, [FromBody] UpdateTaxInformationDto dto)
    {
        var command = new UpdateTaxInformationCommand(
            employeeId,
            dto.FederalFilingStatus,
            dto.FederalAllowances,
            dto.AdditionalFederalWithholding,
            dto.State,
            dto.StateFilingStatus,
            dto.StateAllowances,
            dto.AdditionalStateWithholding);

        var result = await _mediator.Send(command);
        return Ok(result);
    }
}
