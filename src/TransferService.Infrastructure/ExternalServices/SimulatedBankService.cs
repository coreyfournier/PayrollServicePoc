using TransferService.Application.Interfaces;

namespace TransferService.Infrastructure.ExternalServices;

public class SimulatedBankService : IBankTransferService
{
    private static readonly Random Random = new();

    public async Task<BankTransferResult> ExecuteTransferAsync(Guid transferId, decimal amount, Guid bankAccountId)
    {
        // Simulate bank processing time (1-5 seconds)
        await Task.Delay(Random.Next(1000, 5000));

        // ~20% failure rate
        if (Random.NextDouble() < 0.2)
        {
            return new BankTransferResult(false, null, "Bank transfer declined: insufficient funds at bank.");
        }

        var referenceId = $"BNK-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8]}";
        return new BankTransferResult(true, referenceId, null);
    }
}
