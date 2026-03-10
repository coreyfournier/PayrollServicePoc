namespace TransferService.Application.Interfaces;

public interface IBalanceService
{
    Task<BalanceInfo?> GetCurrentBalanceAsync(Guid employeeId, long payPeriodNumber);
}

public record BalanceInfo(decimal NetPay, long PayPeriodNumber);
