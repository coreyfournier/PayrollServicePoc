using Dapr.Workflow;
using TransferService.Application.Interfaces;
using TransferService.Domain.Repositories;

namespace TransferService.Api.Workflows.Activities;

public record CompleteTransferInput(Guid TransferId, string ExternalReferenceId);

public class CompleteTransferActivity : WorkflowActivity<CompleteTransferInput, object?>
{
    private readonly ITransferRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public CompleteTransferActivity(ITransferRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public override async Task<object?> RunAsync(WorkflowActivityContext context, CompleteTransferInput input)
    {
        var transfer = await _repository.GetByIdAsync(input.TransferId)
            ?? throw new InvalidOperationException($"Transfer {input.TransferId} not found.");

        transfer.MarkCompleted(input.ExternalReferenceId);

        await _unitOfWork.ExecuteAsync(
            async () => await _repository.UpdateAsync(transfer),
            transfer);

        return null;
    }
}
