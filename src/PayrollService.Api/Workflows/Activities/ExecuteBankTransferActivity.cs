using Dapr.Workflow;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;

namespace PayrollService.Api.Workflows.Activities;

public record ExecuteBankTransferInput(Guid TransferId, decimal Amount, Guid BankAccountId);
public record BankTransferActivityResult(bool Success, string? ExternalReferenceId, string? ErrorMessage);

public class ExecuteBankTransferActivity : WorkflowActivity<ExecuteBankTransferInput, BankTransferActivityResult>
{
    private readonly IBankTransferService _bankService;
    private readonly IBankAccountRepository _bankAccountRepository;

    public ExecuteBankTransferActivity(IBankTransferService bankService, IBankAccountRepository bankAccountRepository)
    {
        _bankService = bankService;
        _bankAccountRepository = bankAccountRepository;
    }

    public override async Task<BankTransferActivityResult> RunAsync(WorkflowActivityContext context, ExecuteBankTransferInput input)
    {
        var bankAccount = await _bankAccountRepository.GetByIdAsync(input.BankAccountId)
            ?? throw new InvalidOperationException($"Bank account {input.BankAccountId} not found.");

        var result = await _bankService.ExecuteTransferAsync(
            input.TransferId,
            input.Amount,
            bankAccount.RoutingNumber,
            bankAccount.AccountNumberMasked);

        return new BankTransferActivityResult(result.Success, result.ExternalReferenceId, result.ErrorMessage);
    }
}
