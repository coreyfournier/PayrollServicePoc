using Dapr.Actors;
using Dapr.Actors.Runtime;
using Dapr.Workflow;
using Microsoft.Extensions.Options;
using PayrollService.Application.Interfaces;
using PayrollService.Application.Options;
using PayrollService.Api.Workflows;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Repositories;
using PayrollService.Domain.ValueObjects;

namespace PayrollService.Api.Actors;

/// <summary>
/// Dapr Actor keyed by employeeId. Single-threaded guarantee per actor ID
/// eliminates concurrent transfer races without manual locking.
/// </summary>
public class TransferActor : Actor, ITransferActor
{
    private readonly ITransferRepository _transferRepository;
    private readonly IBankAccountRepository _bankAccountRepository;
    private readonly ITransferUnitOfWork _unitOfWork;
    private readonly DaprWorkflowClient _workflowClient;
    private readonly TransferLimitsOptions _options;

    public TransferActor(
        ActorHost host,
        ITransferRepository transferRepository,
        IBankAccountRepository bankAccountRepository,
        ITransferUnitOfWork unitOfWork,
        DaprWorkflowClient workflowClient,
        IOptions<TransferLimitsOptions> options)
        : base(host)
    {
        _transferRepository = transferRepository;
        _bankAccountRepository = bankAccountRepository;
        _unitOfWork = unitOfWork;
        _workflowClient = workflowClient;
        _options = options.Value;
    }

    public async Task<TransferActorResult> InitiateTransferAsync(TransferActorRequest request)
    {
        var employeeId = Guid.Parse(Id.GetId());

        // Validate bank account exists and is active
        var bankAccount = await _bankAccountRepository.GetByIdAsync(request.BankAccountId);
        if (bankAccount == null || !bankAccount.IsActive)
            return new TransferActorResult(false, null, "Bank account not found or inactive.");

        if (bankAccount.EmployeeId != employeeId)
            return new TransferActorResult(false, null, "Bank account does not belong to this employee.");

        // Check transfer limits
        var periodTransfers = await _transferRepository.GetByEmployeeAndPayPeriodAsync(employeeId, request.PayPeriodNumber);
        var activeTransfers = periodTransfers.Where(t => t.Status != TransferStatus.Failed).ToList();

        var currentCount = activeTransfers.Count;
        var currentAmount = activeTransfers.Sum(t => t.Amount);

        var todayStart = DateTime.UtcNow.Date;
        var transfersToday = await _transferRepository.GetCountByEmployeeAndDateAsync(employeeId, todayStart);

        var limits = new TransferLimits(_options.MaxPerPayPeriod, _options.MaxAmountPerPayPeriod, _options.MaxPerDay);
        var validation = limits.Validate(currentCount, currentAmount, request.Amount, transfersToday);

        if (!validation.CanTransfer)
            return new TransferActorResult(false, null, string.Join(" ", validation.Reasons));

        // Create transfer entity
        var transfer = Transfer.Create(employeeId, request.Amount, request.PayPeriodNumber, request.BankAccountId);

        await _unitOfWork.ExecuteAsync(
            async () => await _transferRepository.AddAsync(transfer),
            transfer);

        // Start the workflow
        var workflowInput = new TransferWorkflowInput(
            transfer.Id, employeeId, request.Amount, request.PayPeriodNumber, request.BankAccountId);

        await _workflowClient.ScheduleNewWorkflowAsync(
            nameof(TransferWorkflow),
            $"transfer-{transfer.Id}",
            workflowInput);

        return new TransferActorResult(true, transfer.Id, null);
    }
}
