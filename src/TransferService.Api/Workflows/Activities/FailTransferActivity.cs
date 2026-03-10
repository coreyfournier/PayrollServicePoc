using Dapr.Workflow;
using TransferService.Application.Interfaces;
using TransferService.Domain.Repositories;

namespace TransferService.Api.Workflows.Activities;

public record FailTransferInput(Guid TransferId, string Reason);

public class FailTransferActivity : WorkflowActivity<FailTransferInput, object?>
{
    private readonly ITransferRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public FailTransferActivity(ITransferRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public override async Task<object?> RunAsync(WorkflowActivityContext context, FailTransferInput input)
    {
        var transfer = await _repository.GetByIdAsync(input.TransferId)
            ?? throw new InvalidOperationException($"Transfer {input.TransferId} not found.");

        transfer.MarkFailed(input.Reason);

        await _unitOfWork.ExecuteAsync(
            async () => await _repository.UpdateAsync(transfer),
            transfer);

        return null;
    }
}
