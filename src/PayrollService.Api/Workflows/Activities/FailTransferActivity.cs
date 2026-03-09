using Dapr.Workflow;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;

namespace PayrollService.Api.Workflows.Activities;

public record FailTransferInput(Guid TransferId, string Reason);

public class FailTransferActivity : WorkflowActivity<FailTransferInput, object?>
{
    private readonly ITransferRepository _transferRepository;
    private readonly ITransferUnitOfWork _unitOfWork;

    public FailTransferActivity(ITransferRepository transferRepository, ITransferUnitOfWork unitOfWork)
    {
        _transferRepository = transferRepository;
        _unitOfWork = unitOfWork;
    }

    public override async Task<object?> RunAsync(WorkflowActivityContext context, FailTransferInput input)
    {
        var transfer = await _transferRepository.GetByIdAsync(input.TransferId)
            ?? throw new InvalidOperationException($"Transfer {input.TransferId} not found.");

        transfer.MarkFailed(input.Reason);

        await _unitOfWork.ExecuteAsync(
            async () => await _transferRepository.UpdateAsync(transfer),
            transfer);

        return null;
    }
}
