using MediatR;
using Microsoft.AspNetCore.Mvc;
using PayrollService.Application.Commands.BankAccount;
using PayrollService.Application.DTOs;
using PayrollService.Application.Queries.BankAccount;

namespace PayrollService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BankAccountsController : ControllerBase
{
    private readonly IMediator _mediator;

    public BankAccountsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("employee/{employeeId:guid}")]
    public async Task<ActionResult<IEnumerable<BankAccountDto>>> GetByEmployee(Guid employeeId)
    {
        var result = await _mediator.Send(new GetBankAccountsByEmployeeQuery(employeeId));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<BankAccountDto>> Create([FromBody] CreateBankAccountDto dto)
    {
        var command = new CreateBankAccountCommand(
            dto.EmployeeId,
            dto.BankName,
            dto.AccountNumberMasked,
            dto.RoutingNumber,
            dto.AccountType);

        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetByEmployee), new { employeeId = result.EmployeeId }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BankAccountDto>> Update(Guid id, [FromBody] UpdateBankAccountDto dto)
    {
        var command = new UpdateBankAccountCommand(
            id,
            dto.BankName,
            dto.AccountNumberMasked,
            dto.RoutingNumber,
            dto.AccountType);

        var result = await _mediator.Send(command);
        return Ok(result);
    }
}
