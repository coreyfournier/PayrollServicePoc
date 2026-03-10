using Dapr.Workflow;
using TransferService.Application.Interfaces;
using TransferService.Domain.Repositories;

namespace TransferService.Api.Workflows.Activities;

public record UpdateTransferStatusInput(Guid TransferId);

public class UpdateTransferStatusActivity : WorkflowActivity<UpdateTransferStatusInput, object?>
{
    private readonly ITransferRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateTransferStatusActivity(ITransferRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public override async Task<object?> RunAsync(WorkflowActivityContext context, UpdateTransferStatusInput input)
    {
        var transfer = await _repository.GetByIdAsync(input.TransferId)
            ?? throw new InvalidOperationException($"Transfer {input.TransferId} not found.");

        transfer.MarkProcessing();

        await _unitOfWork.ExecuteAsync(
            async () => await _repository.UpdateAsync(transfer),
            transfer);

        return null;
    }
}
