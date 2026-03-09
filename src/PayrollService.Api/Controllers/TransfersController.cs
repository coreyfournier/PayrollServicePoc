using Dapr.Actors;
using Dapr.Actors.Client;
using Dapr.Workflow;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using PayrollService.Api.Actors;
using PayrollService.Application.DTOs;
using PayrollService.Application.Queries.Transfer;

namespace PayrollService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TransfersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IActorProxyFactory _actorProxyFactory;
    private readonly DaprWorkflowClient _workflowClient;

    public TransfersController(IMediator mediator, IActorProxyFactory actorProxyFactory, DaprWorkflowClient workflowClient)
    {
        _mediator = mediator;
        _actorProxyFactory = actorProxyFactory;
        _workflowClient = workflowClient;
    }

    [HttpPost]
    public async Task<ActionResult<TransferActorResult>> Initiate([FromBody] InitiateTransferDto dto)
    {
        var actorId = new ActorId(dto.EmployeeId.ToString());
        var actor = _actorProxyFactory.CreateActorProxy<ITransferActor>(actorId, "TransferActor");

        var result = await actor.InitiateTransferAsync(
            new TransferActorRequest(dto.Amount, dto.PayPeriodNumber, dto.BankAccountId));

        if (!result.Success)
            return BadRequest(result);

        return Accepted(result);
    }

    [HttpGet("employee/{employeeId:guid}")]
    public async Task<ActionResult<IEnumerable<TransferDto>>> GetByEmployee(Guid employeeId)
    {
        var result = await _mediator.Send(new GetTransfersByEmployeeQuery(employeeId));
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TransferDto>> GetById(Guid id)
    {
        var result = await _mediator.Send(new GetTransfersByEmployeeQuery(id));
        var transfer = result.FirstOrDefault();
        if (transfer == null)
            return NotFound();
        return Ok(transfer);
    }

    [HttpGet("employee/{employeeId:guid}/limits")]
    public async Task<ActionResult<TransferLimitsDto>> GetLimits(Guid employeeId, [FromQuery] long payPeriodNumber)
    {
        var result = await _mediator.Send(new GetTransferLimitsQuery(employeeId, payPeriodNumber));
        return Ok(result);
    }

    [HttpPost("{id:guid}/accept")]
    public async Task<ActionResult> AcceptBalanceChange(Guid id, [FromBody] AcceptBalanceChangeDto dto)
    {
        var workflowId = $"transfer-{id}";
        await _workflowClient.RaiseEventAsync(workflowId, "BalanceAccepted", dto.Accepted);
        return Ok();
    }

    /// <summary>
    /// Dapr subscription endpoint for transfer-requests topic (async commands from ListenerApi).
    /// </summary>
    [HttpPost("process-request")]
    [Dapr.Topic("kafka-pubsub", "transfer-requests")]
    public async Task<ActionResult> ProcessTransferRequest([FromBody] TransferRequestEvent request)
    {
        var actorId = new ActorId(request.EmployeeId.ToString());
        var actor = _actorProxyFactory.CreateActorProxy<ITransferActor>(actorId, "TransferActor");

        await actor.InitiateTransferAsync(
            new TransferActorRequest(request.Amount, request.PayPeriodNumber, request.BankAccountId));

        return Ok();
    }
}

public record TransferRequestEvent(
    Guid EmployeeId,
    decimal Amount,
    long PayPeriodNumber,
    Guid BankAccountId);

public record AcceptBalanceChangeDto(bool Accepted);
