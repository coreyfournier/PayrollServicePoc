using MediatR;
using Microsoft.AspNetCore.Mvc;
using PayrollService.Application.DTOs;
using PayrollService.Application.Queries.EarlyWageAccess;

namespace PayrollService.Api.Controllers;

[ApiController]
[Route("api/v1/employees")]
public class EarlyWageAccessController : ControllerBase
{
    private readonly IMediator _mediator;

    public EarlyWageAccessController(IMediator mediator) => _mediator = mediator;

    [HttpGet("{employeeId:guid}/balance")]
    public async Task<IActionResult> GetBalance(Guid employeeId, [FromQuery] bool includeBreakdown = false)
    {
        var result = await _mediator.Send(new GetEwaBalanceQuery(employeeId, includeBreakdown));

        return result.StatusCode switch
        {
            200 => Ok(result.Balance),
            404 => NotFound(new { error = result.ErrorCode, message = result.ErrorMessage }),
            422 => UnprocessableEntity(new { error = result.ErrorCode, message = result.ErrorMessage }),
            _ => StatusCode(500, new { error = "INTERNAL_ERROR", message = "An unexpected error occurred." })
        };
    }
}
