using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using TransferService.Application.Interfaces;
using TransferService.Application.Messages;
using TransferService.Domain.Entities;
using TransferService.Domain.Repositories;

namespace TransferService.Api.Sagas;

public class TransferStateMachine : MassTransitStateMachine<TransferState>
{
    public State Validating { get; private set; } = default!;
    public State VerifyingBalance { get; private set; } = default!;
    public State AwaitingConfirmation { get; private set; } = default!;
    public State Processing { get; private set; } = default!;
    public State Completed { get; private set; } = default!;
    public State Failed { get; private set; } = default!;

    public Event<InitiateTransferMessage> InitiateTransfer { get; private set; } = default!;
    public Event<TransferValidated> TransferValidatedEvent { get; private set; } = default!;
    public Event<TransferValidationFailed> TransferValidationFailedEvent { get; private set; } = default!;
    public Event<BalanceVerified> BalanceVerifiedEvent { get; private set; } = default!;
    public Event<BalanceInsufficient> BalanceInsufficientEvent { get; private set; } = default!;
    public Event<AcceptBalanceMessage> AcceptBalance { get; private set; } = default!;
    public Event<ConfirmationTimedOut> ConfirmationTimedOutEvent { get; private set; } = default!;
    public Event<BankTransferCompleted> BankTransferCompletedEvent { get; private set; } = default!;
    public Event<BankTransferFailed> BankTransferFailedEvent { get; private set; } = default!;
    public Event<RetryBankTransfer> RetryBankTransferEvent { get; private set; } = default!;

    public Schedule<TransferState, ConfirmationTimedOut> ConfirmationTimeout { get; private set; } = default!;

    public TransferStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => InitiateTransfer, x => x.CorrelateById(ctx => ctx.Message.TransferId));
        Event(() => TransferValidatedEvent, x => x.CorrelateById(ctx => ctx.Message.TransferId));
        Event(() => TransferValidationFailedEvent, x => x.CorrelateById(ctx => ctx.Message.TransferId));
        Event(() => BalanceVerifiedEvent, x => x.CorrelateById(ctx => ctx.Message.TransferId));
        Event(() => BalanceInsufficientEvent, x => x.CorrelateById(ctx => ctx.Message.TransferId));
        Event(() => AcceptBalance, x => x.CorrelateById(ctx => ctx.Message.TransferId));
        Event(() => ConfirmationTimedOutEvent, x => x.CorrelateById(ctx => ctx.Message.TransferId));
        Event(() => BankTransferCompletedEvent, x => x.CorrelateById(ctx => ctx.Message.TransferId));
        Event(() => BankTransferFailedEvent, x => x.CorrelateById(ctx => ctx.Message.TransferId));
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
                .TransitionTo(Validating)
                .ThenAsync(async ctx =>
                {
                    var sp = GetServiceProvider(ctx);
                    var validationService = sp.GetRequiredService<ITransferValidationService>();
                    var result = await validationService.ValidateAsync(
                        new TransferValidationRequest(
                            ctx.Saga.EmployeeId,
                            ctx.Saga.Amount,
                            ctx.Saga.PayPeriodNumber,
                            ctx.Saga.BankAccountId));

                    if (result.CanTransfer)
                        await ctx.Publish(new TransferValidated(ctx.Saga.CorrelationId));
                    else
                        await ctx.Publish(new TransferValidationFailed(ctx.Saga.CorrelationId, string.Join(" ", result.Reasons)));
                })
        );

        During(Validating,
            When(TransferValidatedEvent)
                .TransitionTo(VerifyingBalance)
                .ThenAsync(async ctx =>
                {
                    var sp = GetServiceProvider(ctx);
                    var balanceService = sp.GetRequiredService<IBalanceService>();
                    var balance = await balanceService.GetCurrentBalanceAsync(ctx.Saga.EmployeeId, ctx.Saga.PayPeriodNumber);

                    if (balance == null || balance.NetPay >= ctx.Saga.Amount)
                    {
                        var currentBalance = balance?.NetPay ?? 0;
                        await ctx.Publish(new BalanceVerified(ctx.Saga.CorrelationId, currentBalance));
                    }
                    else
                    {
                        await ctx.Publish(new BalanceInsufficient(ctx.Saga.CorrelationId, balance.NetPay));
                    }
                }),
            When(TransferValidationFailedEvent)
                .TransitionTo(Failed)
                .ThenAsync(async ctx =>
                {
                    ctx.Saga.FailureReason = ctx.Message.Reason;
                    var sp = GetServiceProvider(ctx);
                    var publisher = sp.GetRequiredService<ITransferEventPublisher>();
                    var repo = sp.GetRequiredService<ITransferRepository>();

                    var transfer = Transfer.Create(
                        ctx.Saga.EmployeeId, ctx.Saga.Amount, ctx.Saga.PayPeriodNumber, ctx.Saga.BankAccountId, ctx.Saga.CorrelationId);
                    transfer.MarkFailed(ctx.Message.Reason);
                    await repo.AddAsync(transfer);
                    await publisher.PublishAsync(transfer);
                })
                .Finalize()
        );

        During(VerifyingBalance,
            When(BalanceVerifiedEvent)
                .TransitionTo(Processing)
                .ThenAsync(async ctx =>
                {
                    ctx.Saga.CurrentBalance = ctx.Message.CurrentBalance;
                    var sp = GetServiceProvider(ctx);
                    var publisher = sp.GetRequiredService<ITransferEventPublisher>();
                    var repo = sp.GetRequiredService<ITransferRepository>();

                    var transfer = Transfer.Create(
                        ctx.Saga.EmployeeId, ctx.Saga.Amount, ctx.Saga.PayPeriodNumber, ctx.Saga.BankAccountId, ctx.Saga.CorrelationId);
                    transfer.MarkProcessing();
                    await repo.AddAsync(transfer);
                    await publisher.PublishAsync(transfer);

                    // Execute bank transfer
                    await ExecuteBankTransfer(ctx);
                }),
            When(BalanceInsufficientEvent)
                .TransitionTo(AwaitingConfirmation)
                .ThenAsync(async ctx =>
                {
                    ctx.Saga.CurrentBalance = ctx.Message.CurrentBalance;
                    var sp = GetServiceProvider(ctx);
                    var publisher = sp.GetRequiredService<ITransferEventPublisher>();
                    var repo = sp.GetRequiredService<ITransferRepository>();

                    var transfer = Transfer.Create(
                        ctx.Saga.EmployeeId, ctx.Saga.Amount, ctx.Saga.PayPeriodNumber, ctx.Saga.BankAccountId, ctx.Saga.CorrelationId);
                    transfer.MarkAwaitingConfirmation(ctx.Message.CurrentBalance);
                    await repo.AddAsync(transfer);
                    await publisher.PublishAsync(transfer);
                })
                .Schedule(ConfirmationTimeout, ctx => ctx.Init<ConfirmationTimedOut>(new ConfirmationTimedOut(ctx.Saga.CorrelationId)))
        );

        During(AwaitingConfirmation,
            When(AcceptBalance, ctx => ctx.Message.Accepted)
                .Unschedule(ConfirmationTimeout)
                .TransitionTo(Processing)
                .ThenAsync(async ctx =>
                {
                    var sp = GetServiceProvider(ctx);
                    var publisher = sp.GetRequiredService<ITransferEventPublisher>();
                    var repo = sp.GetRequiredService<ITransferRepository>();

                    var transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);
                    if (transfer != null)
                    {
                        transfer.MarkProcessing();
                        await repo.UpdateAsync(transfer);
                        await publisher.PublishAsync(transfer);
                    }

                    // Execute bank transfer
                    await ExecuteBankTransfer(ctx);
                }),
            When(AcceptBalance, ctx => !ctx.Message.Accepted)
                .Unschedule(ConfirmationTimeout)
                .TransitionTo(Failed)
                .ThenAsync(async ctx =>
                {
                    ctx.Saga.FailureReason = "Transfer rejected by user.";
                    var sp = GetServiceProvider(ctx);
                    var publisher = sp.GetRequiredService<ITransferEventPublisher>();
                    var repo = sp.GetRequiredService<ITransferRepository>();

                    var transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);
                    if (transfer != null)
                    {
                        transfer.MarkFailed("Transfer rejected by user.");
                        await repo.UpdateAsync(transfer);
                        await publisher.PublishAsync(transfer);
                    }
                })
                .Finalize(),
            When(ConfirmationTimedOutEvent)
                .TransitionTo(Failed)
                .ThenAsync(async ctx =>
                {
                    ctx.Saga.FailureReason = "Transfer auto-cancelled: balance change not accepted within 24 hours.";
                    var sp = GetServiceProvider(ctx);
                    var publisher = sp.GetRequiredService<ITransferEventPublisher>();
                    var repo = sp.GetRequiredService<ITransferRepository>();

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

        During(Processing,
            When(BankTransferCompletedEvent)
                .TransitionTo(Completed)
                .ThenAsync(async ctx =>
                {
                    ctx.Saga.ExternalReferenceId = ctx.Message.ExternalReferenceId;
                    var sp = GetServiceProvider(ctx);
                    var publisher = sp.GetRequiredService<ITransferEventPublisher>();
                    var repo = sp.GetRequiredService<ITransferRepository>();

                    var transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);
                    if (transfer != null)
                    {
                        transfer.MarkCompleted(ctx.Message.ExternalReferenceId);
                        await repo.UpdateAsync(transfer);
                        await publisher.PublishAsync(transfer);
                    }
                })
                .Finalize(),
            When(BankTransferFailedEvent)
                .TransitionTo(Failed)
                .ThenAsync(async ctx =>
                {
                    ctx.Saga.FailureReason = ctx.Message.Reason;
                    var sp = GetServiceProvider(ctx);
                    var publisher = sp.GetRequiredService<ITransferEventPublisher>();
                    var repo = sp.GetRequiredService<ITransferRepository>();

                    var transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);
                    if (transfer != null)
                    {
                        transfer.MarkFailed(ctx.Message.Reason);
                        await repo.UpdateAsync(transfer);
                        await publisher.PublishAsync(transfer);
                    }
                })
                .Finalize(),
            When(RetryBankTransferEvent)
                .ThenAsync(async ctx => await ExecuteBankTransfer(ctx))
        );

        SetCompletedWhenFinalized();
    }

    private static IServiceProvider GetServiceProvider<TSaga, TMessage>(BehaviorContext<TSaga, TMessage> ctx)
        where TSaga : class, SagaStateMachineInstance
        where TMessage : class
    {
        return ctx.GetPayload<IServiceProvider>();
    }

    private static async Task ExecuteBankTransfer<T>(BehaviorContext<TransferState, T> ctx) where T : class
    {
        var sp = ctx.GetPayload<IServiceProvider>();
        var bankService = sp.GetRequiredService<IBankTransferService>();
        var result = await bankService.ExecuteTransferAsync(
            ctx.Saga.CorrelationId, ctx.Saga.Amount, ctx.Saga.BankAccountId);

        if (result.Success)
        {
            await ctx.Publish(new BankTransferCompleted(ctx.Saga.CorrelationId, result.ExternalReferenceId!));
        }
        else
        {
            ctx.Saga.RetryCount++;
            if (ctx.Saga.RetryCount >= 3)
            {
                await ctx.Publish(new BankTransferFailed(ctx.Saga.CorrelationId,
                    result.ErrorMessage ?? "Bank transfer failed after all retries."));
            }
            else
            {
                // Schedule retry with exponential backoff
                var delay = TimeSpan.FromSeconds(Math.Pow(2, ctx.Saga.RetryCount));
                await ctx.SchedulePublish(delay, new RetryBankTransfer(ctx.Saga.CorrelationId));
            }
        }
    }
}
