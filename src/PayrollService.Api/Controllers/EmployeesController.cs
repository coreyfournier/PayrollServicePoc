using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayrollService.Application.Commands.Employee;
using PayrollService.Application.DTOs;
using PayrollService.Application.Queries.Employee;

namespace PayrollService.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class EmployeesController : ControllerBase
{
    private readonly IMediator _mediator;

    public EmployeesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<EmployeeDto>>> GetAll()
    {
        var result = await _mediator.Send(new GetEmployeesQuery());
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmployeeDto>> GetById(Guid id)
    {
        var result = await _mediator.Send(new GetEmployeeByIdQuery(id));
        if (result == null)
            return NotFound();
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<EmployeeDto>> Create([FromBody] CreateEmployeeDto dto)
    {
        var command = new CreateEmployeeCommand(
            dto.FirstName,
            dto.LastName,
            dto.Email,
            dto.PayType,
            dto.PayRate,
            dto.HireDate,
            dto.PayPeriodHours);

        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EmployeeDto>> Update(Guid id, [FromBody] UpdateEmployeeDto dto)
    {
        var command = new UpdateEmployeeCommand(
            id,
            dto.FirstName,
            dto.LastName,
            dto.Email,
            dto.PayType,
            dto.PayRate,
            dto.PayPeriodHours);

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id)
    {
        await _mediator.Send(new DeleteEmployeeCommand(id));
        return NoContent();
    }
}
