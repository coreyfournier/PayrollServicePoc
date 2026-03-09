using Dapr.Workflow;
using PayrollService.Application.Interfaces;

namespace PayrollService.Api.Workflows.Activities;

public record VerifyBalanceInput(Guid TransferId, Guid EmployeeId, decimal TransferAmount, long PayPeriodNumber);
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
        {
            // If we can't verify, proceed (fail-open for POC — balance service may be unavailable)
            return new VerifyBalanceResult(true, input.TransferAmount);
        }

        return new VerifyBalanceResult(balance.NetPay >= input.TransferAmount, balance.NetPay);
    }
}
