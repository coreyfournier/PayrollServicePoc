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

    [HttpGet("recent")]
    public async Task<ActionResult<IEnumerable<TransferDto>>> GetRecent([FromQuery] int limit = 50, [FromQuery] string? status = null)
    {
        var result = await _mediator.Send(new GetRecentTransfersQuery(limit, status));
        return Ok(result);
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

    [HttpGet("{id:guid}/workflow")]
    public async Task<ActionResult> GetWorkflowState(Guid id)
    {
        var workflowId = $"transfer-{id}";
        try
        {
            var state = await _workflowClient.GetWorkflowStateAsync(workflowId);
            if (state == null || !state.Exists)
                return NotFound(new { error = "Workflow not found" });

            object? input = null;
            object? output = null;
            object? failureDetails = null;
            try { input = state.ReadInputAs<object>(); } catch { }
            try { output = state.ReadOutputAs<object>(); } catch { }
            try { failureDetails = state.FailureDetails; } catch { }

            return Ok(new
            {
                instanceId = workflowId,
                runtimeStatus = state.RuntimeStatus.ToString(),
                createdAt = state.CreatedAt,
                lastUpdatedAt = state.LastUpdatedAt,
                input,
                output,
                failureDetails,
            });
        }
        catch (Exception ex)
        {
            return NotFound(new { error = $"Workflow not found: {ex.Message}" });
        }
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

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(body);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return BadRequest("Invalid JSON payload");
        }

        // Extract payload from CloudEvent wrapper if present
        var payload = root;
        if (root.TryGetProperty("data", out var dataElement))
        {
            if (dataElement.ValueKind == JsonValueKind.String)
            {
                try { payload = JsonDocument.Parse(dataElement.GetString()!).RootElement; }
                catch { return BadRequest("Invalid data payload"); }
            }
            else
            {
                payload = dataElement;
            }
        }

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Route by action type
        var action = payload.TryGetProperty("Action", out var actionProp)
            ? actionProp.GetString()
            : payload.TryGetProperty("action", out var actionPropLower)
                ? actionPropLower.GetString()
                : null;

        if (action == "accept-balance")
        {
            // Accept/reject balance change — raise workflow event
            var cmd = JsonSerializer.Deserialize<AcceptBalanceCommand>(payload.GetRawText(), opts);
            if (cmd == null || cmd.TransferId == Guid.Empty)
                return BadRequest("Missing required accept-balance fields");

            var workflowId = $"transfer-{cmd.TransferId}";
            await _workflowClient.RaiseEventAsync(workflowId, "BalanceAccepted", cmd.Accepted);
            return Ok();
        }

        // Default: initiate transfer
        var request = JsonSerializer.Deserialize<TransferRequestEvent>(payload.GetRawText(), opts);

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

public record AcceptBalanceCommand(
    Guid TransferId,
    Guid EmployeeId,
    bool Accepted);

public record AcceptBalanceChangeDto(bool Accepted);
