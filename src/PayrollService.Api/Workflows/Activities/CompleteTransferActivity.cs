using Dapr.Workflow;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;

namespace PayrollService.Api.Workflows.Activities;

public record CompleteTransferInput(Guid TransferId, string ExternalReferenceId);

public class CompleteTransferActivity : WorkflowActivity<CompleteTransferInput, object?>
{
    private readonly ITransferRepository _transferRepository;
    private readonly ITransferUnitOfWork _unitOfWork;

    public CompleteTransferActivity(ITransferRepository transferRepository, ITransferUnitOfWork unitOfWork)
    {
        _transferRepository = transferRepository;
        _unitOfWork = unitOfWork;
    }

    public override async Task<object?> RunAsync(WorkflowActivityContext context, CompleteTransferInput input)
    {
        var transfer = await _transferRepository.GetByIdAsync(input.TransferId)
            ?? throw new InvalidOperationException($"Transfer {input.TransferId} not found.");

        transfer.MarkCompleted(input.ExternalReferenceId);

        await _unitOfWork.ExecuteAsync(
            async () => await _transferRepository.UpdateAsync(transfer),
            transfer);

        return null;
    }
}
