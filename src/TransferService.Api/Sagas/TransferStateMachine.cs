using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using TransferService.Application.Interfaces;
using TransferService.Application.Messages;
using TransferService.Domain.Entities;
using TransferService.Domain.Repositories;

namespace TransferService.Api.Sagas;

public class TransferStateMachine : MassTransitStateMachine<TransferState>
{
    public State AwaitingConfirmation { get; private set; } = default!;
    public State Processing { get; private set; } = default!;
    public State Completed { get; private set; } = default!;
    public State Failed { get; private set; } = default!;

    public Event<InitiateTransferMessage> InitiateTransfer { get; private set; } = default!;
    public Event<AcceptBalanceMessage> AcceptBalance { get; private set; } = default!;
    public Event<RetryBankTransfer> RetryBankTransferEvent { get; private set; } = default!;

    public Schedule<TransferState, ConfirmationTimedOut> ConfirmationTimeout { get; private set; } = default!;

    public TransferStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => InitiateTransfer, x => x.CorrelateById(ctx => ctx.Message.TransferId));
        Event(() => AcceptBalance, x => x.CorrelateById(ctx => ctx.Message.TransferId));
        Event(() => RetryBankTransferEvent, x => x.CorrelateById(ctx => ctx.Message.TransferId));

        Schedule(() => ConfirmationTimeout, instance => instance.ConfirmationTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromHours(24);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.TransferId);
        });

        Initially(
            When(InitiateTransfer)
                .Then(ctx =>
                {
                    ctx.Saga.EmployeeId = ctx.Message.EmployeeId;
                    ctx.Saga.Amount = ctx.Message.Amount;
                    ctx.Saga.PayPeriodNumber = ctx.Message.PayPeriodNumber;
                    ctx.Saga.BankAccountId = ctx.Message.BankAccountId;
                })
                .ThenAsync(async ctx =>
                {
                    using var scope = CreateScope(ctx);

                    // Validate
                    var validationService = scope.ServiceProvider.GetRequiredService<ITransferValidationService>();
                    var result = await validationService.ValidateAsync(
                        new TransferValidationRequest(
                            ctx.Saga.EmployeeId,
                            ctx.Saga.Amount,
                            ctx.Saga.PayPeriodNumber,
                            ctx.Saga.BankAccountId));

                    if (!result.CanTransfer)
                    {
                        ctx.Saga.FailureReason = string.Join(" ", result.Reasons);
                        ctx.Saga.ValidationFailed = true;
                        return;
                    }

                    // Balance check
                    var balanceService = scope.ServiceProvider.GetRequiredService<IBalanceService>();
                    var balance = await balanceService.GetCurrentBalanceAsync(ctx.Saga.EmployeeId, ctx.Saga.PayPeriodNumber);

                    if (balance != null && balance.NetPay < ctx.Saga.Amount)
                    {
                        ctx.Saga.CurrentBalance = balance.NetPay;
                        ctx.Saga.BalanceInsufficient = true;
                    }
                    else
                    {
                        ctx.Saga.CurrentBalance = balance?.NetPay ?? 0;
                    }
                })
                .IfElse(ctx => ctx.Saga.ValidationFailed,
                    failed => failed
                        .TransitionTo(Failed)
                        .ThenAsync(async ctx =>
                        {
                            using var scope = CreateScope(ctx);
                            var publisher = scope.ServiceProvider.GetRequiredService<ITransferEventPublisher>();
                            var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

                            var transfer = Transfer.Create(
                                ctx.Saga.EmployeeId, ctx.Saga.Amount, ctx.Saga.PayPeriodNumber, ctx.Saga.BankAccountId, ctx.Saga.CorrelationId);
                            transfer.MarkFailed(ctx.Saga.FailureReason!);
                            await repo.AddAsync(transfer);
                            await publisher.PublishAsync(transfer);
                        })
                        .Finalize(),
                    passed => passed
                        .IfElse(ctx => ctx.Saga.BalanceInsufficient,
                            insufficient => insufficient
                                .TransitionTo(AwaitingConfirmation)
                                .ThenAsync(async ctx =>
                                {
                                    using var scope = CreateScope(ctx);
                                    var publisher = scope.ServiceProvider.GetRequiredService<ITransferEventPublisher>();
                                    var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

                                    var transfer = Transfer.Create(
                                        ctx.Saga.EmployeeId, ctx.Saga.Amount, ctx.Saga.PayPeriodNumber, ctx.Saga.BankAccountId, ctx.Saga.CorrelationId);
                                    transfer.MarkAwaitingConfirmation(ctx.Saga.CurrentBalance!.Value);
                                    await repo.AddAsync(transfer);
                                    await publisher.PublishAsync(transfer);
                                })
                                .Schedule(ConfirmationTimeout, ctx => ctx.Init<ConfirmationTimedOut>(new ConfirmationTimedOut(ctx.Saga.CorrelationId))),
                            sufficient => sufficient
                                .TransitionTo(Processing)
                                .ThenAsync(async ctx => await ExecuteBankTransferInline(ctx))
                        )
                )
        );

        During(AwaitingConfirmation,
            When(AcceptBalance, ctx => ctx.Message.Accepted)
                .Unschedule(ConfirmationTimeout)
                .TransitionTo(Processing)
                .ThenAsync(async ctx => await ExecuteBankTransferInline(ctx)),
            When(AcceptBalance, ctx => !ctx.Message.Accepted)
                .Unschedule(ConfirmationTimeout)
                .TransitionTo(Failed)
                .ThenAsync(async ctx =>
                {
                    ctx.Saga.FailureReason = "Transfer rejected by user.";
                    using var scope = CreateScope(ctx);
                    var publisher = scope.ServiceProvider.GetRequiredService<ITransferEventPublisher>();
                    var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

                    var transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);
                    if (transfer != null)
                    {
                        transfer.MarkFailed("Transfer rejected by user.");
                        await repo.UpdateAsync(transfer);
                        await publisher.PublishAsync(transfer);
                    }
                })
                .Finalize(),
            When(ConfirmationTimeout.Received)
                .TransitionTo(Failed)
                .ThenAsync(async ctx =>
                {
                    ctx.Saga.FailureReason = "Transfer auto-cancelled: balance change not accepted within 24 hours.";
                    using var scope = CreateScope(ctx);
                    var publisher = scope.ServiceProvider.GetRequiredService<ITransferEventPublisher>();
                    var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

                    var transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);
                    if (transfer != null)
                    {
                        transfer.MarkFailed("Transfer auto-cancelled: balance change not accepted within 24 hours.");
                        await repo.UpdateAsync(transfer);
                        await publisher.PublishAsync(transfer);
                    }
                })
                .Finalize()
        );

        // Processing state: only entered via scheduled retries
        During(Processing,
            When(RetryBankTransferEvent)
                .ThenAsync(async ctx => await ExecuteBankTransferInline(ctx))
        );

        SetCompletedWhenFinalized();
    }

    /// <summary>
    /// Executes the bank transfer inline, handling the result directly
    /// rather than publishing events (avoids in-memory bus timing issues).
    /// On success: completes transfer and finalizes saga.
    /// On failure with retries left: schedules a RetryBankTransfer.
    /// On failure with no retries: fails transfer and finalizes saga.
    /// </summary>
    private static async Task ExecuteBankTransferInline<T>(BehaviorContext<TransferState, T> ctx) where T : class
    {
        using var scope = CreateScope(ctx);
        var bankService = scope.ServiceProvider.GetRequiredService<IBankTransferService>();
        var publisher = scope.ServiceProvider.GetRequiredService<ITransferEventPublisher>();
        var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

        // Ensure transfer entity exists (first attempt creates it, retries update it)
        var transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);
        if (transfer == null)
        {
            transfer = Transfer.Create(
                ctx.Saga.EmployeeId, ctx.Saga.Amount, ctx.Saga.PayPeriodNumber, ctx.Saga.BankAccountId, ctx.Saga.CorrelationId);
            transfer.MarkProcessing();
            await repo.AddAsync(transfer);
            await publisher.PublishAsync(transfer);
        }
        else if (transfer.Status != Domain.Enums.TransferStatus.Processing)
        {
            transfer.MarkProcessing();
            await repo.UpdateAsync(transfer);
            await publisher.PublishAsync(transfer);
        }

        var result = await bankService.ExecuteTransferAsync(
            ctx.Saga.CorrelationId, ctx.Saga.Amount, ctx.Saga.BankAccountId);

        if (result.Success)
        {
            ctx.Saga.ExternalReferenceId = result.ExternalReferenceId;
            transfer.MarkCompleted(result.ExternalReferenceId!);
            await repo.UpdateAsync(transfer);
            await publisher.PublishAsync(transfer);
            await ctx.SetCompleted();
        }
        else
        {
            ctx.Saga.RetryCount++;
            if (ctx.Saga.RetryCount >= 3)
            {
                var reason = result.ErrorMessage ?? "Bank transfer failed after all retries.";
                ctx.Saga.FailureReason = reason;
                transfer.MarkFailed(reason);
                await repo.UpdateAsync(transfer);
                await publisher.PublishAsync(transfer);
                await ctx.SetCompleted();
            }
            else
            {
                // Schedule retry with exponential backoff
                var delay = TimeSpan.FromSeconds(Math.Pow(2, ctx.Saga.RetryCount));
                await ctx.SchedulePublish(delay, new RetryBankTransfer(ctx.Saga.CorrelationId));
            }
        }
    }

    private static IServiceScope CreateScope<TSaga, TMessage>(BehaviorContext<TSaga, TMessage> ctx)
        where TSaga : class, SagaStateMachineInstance
        where TMessage : class
    {
        var provider = ctx.GetPayload<IServiceProvider>();
        return provider.CreateScope();
    }
}
