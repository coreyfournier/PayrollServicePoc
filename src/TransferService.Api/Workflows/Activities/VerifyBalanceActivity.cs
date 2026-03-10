using Dapr.Workflow;
using TransferService.Application.Interfaces;

namespace TransferService.Api.Workflows.Activities;

public record VerifyBalanceInput(Guid TransferId, Guid EmployeeId, decimal Amount, long PayPeriodNumber);
public record VerifyBalanceResult(bool SufficientBalance, decimal CurrentBalance);

public class VerifyBalanceActivity : WorkflowActivity<VerifyBalanceInput, VerifyBalanceResult>
{
    private readonly IBalanceService _balanceService;

    public VerifyBalanceActivity(IBalanceService balanceService)
    {
        _balanceService = balanceService;
    }

    public override async Task<VerifyBalanceResult> RunAsync(WorkflowActivityContext context, VerifyBalanceInput input)
    {
        var balance = await _balanceService.GetCurrentBalanceAsync(input.EmployeeId, input.PayPeriodNumber);

        if (balance == null)
            return new VerifyBalanceResult(true, 0);

        return new VerifyBalanceResult(balance.NetPay >= input.Amount, balance.NetPay);
    }
}
