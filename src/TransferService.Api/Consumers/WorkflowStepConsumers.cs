using MassTransit;
using Microsoft.Extensions.Logging;
using TransferService.Application.Interfaces;
using TransferService.Application.Messages;

namespace TransferService.Api.Consumers;

public class RunBalanceCheckConsumer : IConsumer<RunBalanceCheck>
{
    private readonly IBalanceService _balanceService;
    private readonly ILogger<RunBalanceCheckConsumer> _logger;

    public RunBalanceCheckConsumer(
        IBalanceService balanceService,
        ILogger<RunBalanceCheckConsumer> logger)
    {
        _balanceService = balanceService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<RunBalanceCheck> context)
    {
        var msg = context.Message;
        _logger.LogInformation("Running balance check for transfer {TransferId}", msg.TransferId);

        // Simulated processing delay
        await Task.Delay(Random.Shared.Next(1000, 5000));

        var balance = await _balanceService.GetCurrentBalanceAsync(msg.EmployeeId, msg.PayPeriodNumber);
        var available = balance?.AvailableBalance ?? 0;
        var sufficient = balance != null && available >= msg.Amount;

        var result = new BalanceCheckCompleted(msg.TransferId, sufficient, available);
        await context.Publish(result);
    }
}

public class RunFraudCheckConsumer : IConsumer<RunFraudCheck>
{
    private readonly ILogger<RunFraudCheckConsumer> _logger;

    public RunFraudCheckConsumer(ILogger<RunFraudCheckConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<RunFraudCheck> context)
    {
        var msg = context.Message;
        _logger.LogInformation("Running fraud check for transfer {TransferId}", msg.TransferId);

        // Simulated fraud check delay
        await Task.Delay(TimeSpan.FromSeconds(4));

        var result = new FraudCheckCompleted(msg.TransferId);
        await context.Publish(result);
    }
}

public class RunBankTransferConsumer : IConsumer<RunBankTransfer>
{
    private readonly IBankTransferService _bankService;
    private readonly ILogger<RunBankTransferConsumer> _logger;

    public RunBankTransferConsumer(
        IBankTransferService bankService,
        ILogger<RunBankTransferConsumer> logger)
    {
        _bankService = bankService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<RunBankTransfer> context)
    {
        var msg = context.Message;
        _logger.LogInformation("Running bank transfer for transfer {TransferId}", msg.TransferId);

        var result = await _bankService.ExecuteTransferAsync(msg.TransferId, msg.Amount, msg.BankAccountId);

        var completed = new BankTransferCompleted(
            msg.TransferId, result.Success, result.ExternalReferenceId, result.ErrorMessage);
        await context.Publish(completed);
    }
}
