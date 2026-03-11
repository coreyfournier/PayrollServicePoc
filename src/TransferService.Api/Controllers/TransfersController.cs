using System.Text.Json;
using Dapr.Actors;
using Dapr.Actors.Client;
using Dapr.Workflow;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using TransferService.Api.Actors;
using TransferService.Application.DTOs;
using TransferService.Application.Queries.Transfer;

namespace TransferService.Api.Controllers;

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

    [HttpPost("process-request")]
    public async Task<ActionResult> ProcessTransferRequest()
    {
        // Read body manually to handle both CloudEvent-wrapped (Dapr outbox) and
        // raw JSON (Debezium CDC outbox) messages without content-type issues.
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        // If Dapr wraps the message in a CloudEvent, extract the "data" field
        TransferRequestEvent? request = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                // CloudEvent envelope — extract data (may be string or object)
                if (dataElement.ValueKind == JsonValueKind.String)
                    request = JsonSerializer.Deserialize<TransferRequestEvent>(dataElement.GetString()!,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                else
                    request = JsonSerializer.Deserialize<TransferRequestEvent>(dataElement.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            else
            {
                // Raw JSON payload (no CloudEvent wrapper)
                request = JsonSerializer.Deserialize<TransferRequestEvent>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }
        catch (JsonException)
        {
            return BadRequest("Invalid JSON payload");
        }

        if (request == null || request.EmployeeId == Guid.Empty)
            return BadRequest("Missing required transfer request fields");

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
