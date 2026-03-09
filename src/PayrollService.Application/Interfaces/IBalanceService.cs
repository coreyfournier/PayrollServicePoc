namespace PayrollService.Application.Interfaces;

public record BalanceInfo(decimal NetPay, long PayPeriodNumber);

public interface IBalanceService
{
    Task<BalanceInfo?> GetCurrentBalanceAsync(Guid employeeId, long payPeriodNumber, CancellationToken cancellationToken = default);
}
