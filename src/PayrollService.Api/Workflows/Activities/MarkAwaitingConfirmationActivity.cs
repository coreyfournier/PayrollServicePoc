using Dapr.Workflow;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;

namespace PayrollService.Api.Workflows.Activities;

public record MarkAwaitingConfirmationInput(Guid TransferId, decimal CurrentBalance);

public class MarkAwaitingConfirmationActivity : WorkflowActivity<MarkAwaitingConfirmationInput, object?>
{
    private readonly ITransferRepository _transferRepository;
    private readonly ITransferUnitOfWork _unitOfWork;

    public MarkAwaitingConfirmationActivity(ITransferRepository transferRepository, ITransferUnitOfWork unitOfWork)
    {
        _transferRepository = transferRepository;
        _unitOfWork = unitOfWork;
    }

    public override async Task<object?> RunAsync(WorkflowActivityContext context, MarkAwaitingConfirmationInput input)
    {
        var transfer = await _transferRepository.GetByIdAsync(input.TransferId)
            ?? throw new InvalidOperationException($"Transfer {input.TransferId} not found.");

        transfer.MarkAwaitingConfirmation(input.CurrentBalance);

        await _unitOfWork.ExecuteAsync(
            async () => await _transferRepository.UpdateAsync(transfer),
            transfer);

        return null;
    }
}
