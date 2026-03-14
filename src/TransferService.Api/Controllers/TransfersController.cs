using System.Text.Json;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using TransferService.Api.Sagas;
using TransferService.Application.DTOs;
using TransferService.Application.Interfaces;
using TransferService.Application.Messages;
using TransferService.Application.Queries.Transfer;

namespace TransferService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TransfersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ITransferValidationService _validationService;
    private readonly IMongoCollection<TransferState> _sagaCollection;

    public TransfersController(
        IMediator mediator,
        IPublishEndpoint publishEndpoint,
        ITransferValidationService validationService,
        IMongoDatabase mongoDatabase)
    {
        _mediator = mediator;
        _publishEndpoint = publishEndpoint;
        _validationService = validationService;
        _sagaCollection = mongoDatabase.GetCollection<TransferState>("transfer_sagas");
    }

    [HttpPost("validate")]
    public async Task<ActionResult<TransferValidationResult>> Validate([FromBody] InitiateTransferDto dto)
    {
        var result = await _validationService.ValidateAsync(
            new TransferValidationRequest(dto.EmployeeId, dto.Amount, dto.PayPeriodNumber, dto.BankAccountId));

        if (!result.CanTransfer)
            return Ok(result);

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<TransferInitiateResult>> Initiate([FromBody] InitiateTransferDto dto)
    {
        var transferId = Guid.NewGuid();

        await _publishEndpoint.Publish(new TransferRequested(
            transferId, dto.EmployeeId, dto.Amount, dto.PayPeriodNumber, dto.BankAccountId));

        return Accepted(new TransferInitiateResult(true, transferId, null));
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
        var sagaState = await _sagaCollection.Find(s => s.CorrelationId == id).FirstOrDefaultAsync();
        if (sagaState == null)
            return NotFound(new { error = "Saga state not found" });

        return Ok(new
        {
            instanceId = $"transfer-{id}",
            runtimeStatus = sagaState.CurrentState,
            correlationId = sagaState.CorrelationId,
            employeeId = sagaState.EmployeeId,
            amount = sagaState.Amount,
            retryCount = sagaState.RetryCount,
            externalReferenceId = sagaState.ExternalReferenceId,
            failureReason = sagaState.FailureReason,
        });
    }

    [HttpPost("{id:guid}/accept")]
    public async Task<ActionResult> AcceptBalanceChange(Guid id, [FromBody] AcceptBalanceChangeDto dto)
    {
        // Look up the saga to get the employeeId
        var sagaState = await _sagaCollection.Find(s => s.CorrelationId == id).FirstOrDefaultAsync();
        var employeeId = sagaState?.EmployeeId ?? Guid.Empty;

        await _publishEndpoint.Publish(new BalanceAccepted(id, employeeId, dto.Accepted));
        return Ok();
    }
}

public record TransferInitiateResult(bool Success, Guid? TransferId, string? ErrorMessage);
public record AcceptBalanceChangeDto(bool Accepted);
