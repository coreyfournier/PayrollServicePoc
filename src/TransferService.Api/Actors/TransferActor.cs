using Dapr.Actors;
using Dapr.Actors.Runtime;
using Dapr.Workflow;
using TransferService.Application.Interfaces;
using TransferService.Api.Workflows;
using TransferService.Domain.Entities;
using TransferService.Domain.Repositories;

namespace TransferService.Api.Actors;

public class TransferActor : Actor, ITransferActor
{
    private readonly ITransferRepository _transferRepository;
    private readonly ITransferValidationService _validationService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly DaprWorkflowClient _workflowClient;

    public TransferActor(
        ActorHost host,
        ITransferRepository transferRepository,
        ITransferValidationService validationService,
        IUnitOfWork unitOfWork,
        DaprWorkflowClient workflowClient)
        : base(host)
    {
        _transferRepository = transferRepository;
        _validationService = validationService;
        _unitOfWork = unitOfWork;
        _workflowClient = workflowClient;
    }

    public async Task<TransferActorResult> InitiateTransferAsync(TransferActorRequest request)
    {
        var employeeId = Guid.Parse(Id.GetId());

        var validation = await _validationService.ValidateAsync(
            new TransferValidationRequest(employeeId, request.Amount, request.PayPeriodNumber, request.BankAccountId));

        if (!validation.CanTransfer)
            return new TransferActorResult(false, null, string.Join(" ", validation.Reasons));

        var transfer = Transfer.Create(employeeId, request.Amount, request.PayPeriodNumber, request.BankAccountId, request.TransferId);

        await _unitOfWork.ExecuteAsync(
            async () => await _transferRepository.AddAsync(transfer),
            transfer);

        var workflowInput = new TransferWorkflowInput(
            transfer.Id, employeeId, request.Amount, request.PayPeriodNumber, request.BankAccountId);

        await _workflowClient.ScheduleNewWorkflowAsync(
            nameof(TransferWorkflow),
            $"transfer-{transfer.Id}",
            workflowInput);

        return new TransferActorResult(true, transfer.Id, null);
    }
}
