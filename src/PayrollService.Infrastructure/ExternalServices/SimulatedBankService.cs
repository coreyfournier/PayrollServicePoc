using Microsoft.Extensions.Logging;
using PayrollService.Application.Interfaces;

namespace PayrollService.Infrastructure.ExternalServices;

public class SimulatedBankService : IBankTransferService
{
    private readonly ILogger<SimulatedBankService> _logger;

    public SimulatedBankService(ILogger<SimulatedBankService> logger)
    {
        _logger = logger;
    }

    public async Task<BankTransferResult> ExecuteTransferAsync(
        Guid transferId,
        decimal amount,
        string routingNumber,
        string accountNumberMasked,
        CancellationToken cancellationToken = default)
    {
        // Simulate bank processing delay (1-10 seconds)
        var delayMs = Random.Shared.Next(1000, 10001);
        _logger.LogInformation("Simulating bank transfer {TransferId} for ${Amount} — delay {DelayMs}ms",
            transferId, amount, delayMs);

        await Task.Delay(delayMs, cancellationToken);

        // ~20% failure rate
        if (Random.Shared.NextDouble() < 0.2)
        {
            var reason = GetRandomFailureReason();
            _logger.LogWarning("Simulated bank transfer {TransferId} failed: {Reason}", transferId, reason);
            return new BankTransferResult(false, null, reason);
        }

        var externalRef = $"BNK-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";
        _logger.LogInformation("Simulated bank transfer {TransferId} succeeded with ref {ExternalRef}",
            transferId, externalRef);
        return new BankTransferResult(true, externalRef, null);
    }

    private static string GetRandomFailureReason()
    {
        var reasons = new[]
        {
            "Insufficient funds in source account",
            "Bank system temporarily unavailable",
            "Account verification failed",
            "Transaction limit exceeded at bank",
            "Network timeout with banking partner"
        };
        return reasons[Random.Shared.Next(reasons.Length)];
    }
}
