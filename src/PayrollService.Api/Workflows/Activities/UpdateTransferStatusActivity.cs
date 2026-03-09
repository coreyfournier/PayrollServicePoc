using Dapr.Workflow;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;

namespace PayrollService.Api.Workflows.Activities;

public record UpdateTransferStatusInput(Guid TransferId);

public class UpdateTransferStatusActivity : WorkflowActivity<UpdateTransferStatusInput, object?>
{
    private readonly ITransferRepository _transferRepository;
    private readonly ITransferUnitOfWork _unitOfWork;

    public UpdateTransferStatusActivity(ITransferRepository transferRepository, ITransferUnitOfWork unitOfWork)
    {
        _transferRepository = transferRepository;
        _unitOfWork = unitOfWork;
    }

    public override async Task<object?> RunAsync(WorkflowActivityContext context, UpdateTransferStatusInput input)
    {
        var transfer = await _transferRepository.GetByIdAsync(input.TransferId)
            ?? throw new InvalidOperationException($"Transfer {input.TransferId} not found.");

        transfer.MarkProcessing();

        await _unitOfWork.ExecuteAsync(
            async () => await _transferRepository.UpdateAsync(transfer),
            transfer);

        return null;
    }
}
