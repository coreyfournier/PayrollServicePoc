using Dapr.Workflow;
using TransferService.Application.Interfaces;
using TransferService.Domain.Repositories;

namespace TransferService.Api.Workflows.Activities;

public record MarkAwaitingConfirmationInput(Guid TransferId, decimal CurrentBalance);

public class MarkAwaitingConfirmationActivity : WorkflowActivity<MarkAwaitingConfirmationInput, object?>
{
    private readonly ITransferRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public MarkAwaitingConfirmationActivity(ITransferRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public override async Task<object?> RunAsync(WorkflowActivityContext context, MarkAwaitingConfirmationInput input)
    {
        var transfer = await _repository.GetByIdAsync(input.TransferId)
            ?? throw new InvalidOperationException($"Transfer {input.TransferId} not found.");

        transfer.MarkAwaitingConfirmation(input.CurrentBalance);

        await _unitOfWork.ExecuteAsync(
            async () => await _repository.UpdateAsync(transfer),
            transfer);

        return null;
    }
}
