using Dapr.Workflow;
using TransferService.Application.Interfaces;

namespace TransferService.Api.Workflows.Activities;

public record ExecuteBankTransferInput(Guid TransferId, decimal Amount, Guid BankAccountId);
public record BankTransferActivityResult(bool Success, string? ExternalReferenceId, string? ErrorMessage);

public class ExecuteBankTransferActivity : WorkflowActivity<ExecuteBankTransferInput, BankTransferActivityResult>
{
    private readonly IBankTransferService _bankService;

    public ExecuteBankTransferActivity(IBankTransferService bankService)
    {
        _bankService = bankService;
    }

    public override async Task<BankTransferActivityResult> RunAsync(WorkflowActivityContext context, ExecuteBankTransferInput input)
    {
        var result = await _bankService.ExecuteTransferAsync(input.TransferId, input.Amount, input.BankAccountId);
        return new BankTransferActivityResult(result.Success, result.ExternalReferenceId, result.ErrorMessage);
    }
}
