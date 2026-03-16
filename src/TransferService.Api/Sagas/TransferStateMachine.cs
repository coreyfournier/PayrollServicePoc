using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using TransferService.Application.Interfaces;
using TransferService.Application.Messages;
using TransferService.Domain.Entities;
using TransferService.Domain.Exceptions;
using TransferService.Domain.Repositories;
using TransferService.Domain.ValueObjects;

namespace TransferService.Api.Sagas;

public class TransferStateMachine : MassTransitStateMachine<TransferState>
{
    public State AwaitingConfirmation { get; private set; } = default!;
    public State Processing { get; private set; } = default!;
    public State Completed { get; private set; } = default!;
    public State Failed { get; private set; } = default!;

    public Event<TransferRequested> InitiateTransfer { get; private set; } = default!;
    public Event<BalanceAccepted> AcceptBalance { get; private set; } = default!;
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

        // ── Initial: validate + balance check inline, then branch ──
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
                    var validationService = scope.ServiceProvider.GetRequiredService<ITransferValidationService>();
                    var balanceService = scope.ServiceProvider.GetRequiredService<IBalanceService>();

                    // Step 1: Validate
                    var result = await validationService.ValidateAsync(
                        new TransferValidationRequest(
                            ctx.Saga.EmployeeId, ctx.Saga.Amount,
                            ctx.Saga.PayPeriodNumber, ctx.Saga.BankAccountId));

                    if (!result.CanTransfer)
                    {
                        var reason = string.Join(" ", result.Reasons);
                        ctx.Saga.FailureReason = reason;
                        ctx.Saga.TransferOutcome = TransferOutcome.ValidationFailed;
                        ctx.Saga.OutcomeDetail = reason;
                        return;
                    }

                    // Step 2: Balance check
                    var balance = await balanceService.GetCurrentBalanceAsync(
                        ctx.Saga.EmployeeId, ctx.Saga.PayPeriodNumber);

                    if (balance != null && balance.NetPay < ctx.Saga.Amount)
                    {
                        // Balance insufficient → awaiting confirmation
                        ctx.Saga.CurrentBalance = balance.NetPay;
                        ctx.Saga.TransferOutcome = TransferOutcome.BalanceInsufficient;
                    }
                    else
                    {
                        // Balance sufficient → processing
                        ctx.Saga.CurrentBalance = balance?.NetPay ?? 0;
                        ctx.Saga.TransferOutcome = TransferOutcome.BalanceSufficient;
                    }
                })
                .IfElse(ctx => ctx.Saga.TransferOutcome == TransferOutcome.ValidationFailed,
                    // Validation failed → create transfer as failed, finalize
                    fail => fail
                        .TransitionTo(Failed)
                        .ThenAsync(async ctx =>
                        {
                            using var scope = CreateScope(ctx);
                            var publisher = scope.ServiceProvider.GetRequiredService<ITransferEventPublisher>();
                            var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

                            var transfer = Transfer.Create(
                                ctx.Saga.EmployeeId, ctx.Saga.Amount, ctx.Saga.PayPeriodNumber,
                                ctx.Saga.BankAccountId, ctx.Saga.CorrelationId);
                            transfer.FailWorkflowStep(WorkflowStep.Names.Validation, ctx.Saga.OutcomeDetail!);
                            transfer.MarkFailed(ctx.Saga.FailureReason!);
                            await repo.AddAsync(transfer);
                            await publisher.PublishAsync(transfer);
                        })
                        .Finalize(),
                    // Validation passed → check balance outcome
                    pass => pass
                        .IfElse(ctx => ctx.Saga.TransferOutcome == TransferOutcome.BalanceSufficient,
                            // Balance sufficient → create transfer, execute bank transfer inline
                            sufficient => sufficient
                                .ThenAsync(async ctx =>
                                {
                                    using var scope = CreateScope(ctx);
                                    var publisher = scope.ServiceProvider.GetRequiredService<ITransferEventPublisher>();
                                    var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

                                    var transfer = Transfer.Create(
                                        ctx.Saga.EmployeeId, ctx.Saga.Amount, ctx.Saga.PayPeriodNumber,
                                        ctx.Saga.BankAccountId, ctx.Saga.CorrelationId);
                                    transfer.CompleteWorkflowStep(WorkflowStep.Names.Validation);
                                    transfer.CompleteWorkflowStep(WorkflowStep.Names.BalanceCheck);
                                    transfer.AddWorkflowStep(WorkflowStep.Names.BankTransfer, WorkflowStep.Statuses.InProgress);
                                    transfer.AddWorkflowStep(WorkflowStep.Names.Complete, WorkflowStep.Statuses.Pending);
                                    transfer.MarkProcessing();
                                    try
                                    {
                                        await repo.AddAsync(transfer);
                                        await publisher.PublishAsync(transfer);
                                    }
                                    catch (DuplicateInProgressTransferException)
                                    {
                                        ctx.Saga.FailureReason = "A transfer is already in progress for this employee.";
                                        ctx.Saga.TransferOutcome = TransferOutcome.ValidationFailed;
                                        transfer.MarkFailed(ctx.Saga.FailureReason);
                                        await publisher.PublishAsync(transfer);
                                        ctx.Saga.BankTransferSucceeded = false;
                                        return;
                                    }

                                    await ExecuteBankTransferInline(ctx);
                                })
                                .If(ctx => ctx.Saga.BankTransferSucceeded == true,
                                    x => x.TransitionTo(Completed).Finalize())
                                .If(ctx => ctx.Saga.BankTransferSucceeded == false,
                                    x => x.TransitionTo(Failed).Finalize())
                                .If(ctx => ctx.Saga.BankTransferSucceeded == null,
                                    x => x.TransitionTo(Processing)),
                            // Balance insufficient → awaiting confirmation
                            insufficient => insufficient
                                .TransitionTo(AwaitingConfirmation)
                                .ThenAsync(async ctx =>
                                {
                                    using var scope = CreateScope(ctx);
                                    var publisher = scope.ServiceProvider.GetRequiredService<ITransferEventPublisher>();
                                    var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

                                    var transfer = Transfer.Create(
                                        ctx.Saga.EmployeeId, ctx.Saga.Amount, ctx.Saga.PayPeriodNumber,
                                        ctx.Saga.BankAccountId, ctx.Saga.CorrelationId);
                                    transfer.CompleteWorkflowStep(WorkflowStep.Names.Validation);
                                    transfer.CompleteWorkflowStep(WorkflowStep.Names.BalanceCheck,
                                        $"Balance ${ctx.Saga.CurrentBalance:F2} is less than transfer amount");
                                    transfer.AddWorkflowStep(WorkflowStep.Names.AwaitingConfirmation, WorkflowStep.Statuses.InProgress);
                                    transfer.AddWorkflowStep(WorkflowStep.Names.BankTransfer, WorkflowStep.Statuses.Pending);
                                    transfer.AddWorkflowStep(WorkflowStep.Names.Complete, WorkflowStep.Statuses.Pending);
                                    transfer.MarkAwaitingConfirmation(ctx.Saga.CurrentBalance!.Value);
                                    try
                                    {
                                        await repo.AddAsync(transfer);
                                        await publisher.PublishAsync(transfer);
                                    }
                                    catch (DuplicateInProgressTransferException)
                                    {
                                        ctx.Saga.FailureReason = "A transfer is already in progress for this employee.";
                                        ctx.Saga.TransferOutcome = TransferOutcome.ValidationFailed;
                                        transfer.MarkFailed(ctx.Saga.FailureReason);
                                        await publisher.PublishAsync(transfer);
                                    }
                                })
                                .If(ctx => ctx.Saga.TransferOutcome == TransferOutcome.ValidationFailed,
                                    binder => binder.TransitionTo(Failed).Finalize())
                                .Schedule(ConfirmationTimeout, ctx => ctx.Init<ConfirmationTimedOut>(new ConfirmationTimedOut(ctx.Saga.CorrelationId)))
                        )
                )
        );

        // ── AwaitingConfirmation: user accepts, rejects, or timeout ──
        During(AwaitingConfirmation,
            When(AcceptBalance, ctx => ctx.Message.Accepted)
                .Unschedule(ConfirmationTimeout)
                .ThenAsync(async ctx =>
                {
                    using var scope = CreateScope(ctx);
                    var publisher = scope.ServiceProvider.GetRequiredService<ITransferEventPublisher>();
                    var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

                    var transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);
                    if (transfer != null)
                    {
                        transfer.CompleteWorkflowStep(WorkflowStep.Names.AwaitingConfirmation, "Accepted by user");
                        transfer.StartWorkflowStep(WorkflowStep.Names.BankTransfer);
                        transfer.MarkProcessing();
                        await repo.UpdateAsync(transfer);
                        await publisher.PublishAsync(transfer);
                    }

                    await ExecuteBankTransferInline(ctx);
                })
                .If(ctx => ctx.Saga.BankTransferSucceeded == true,
                    x => x.TransitionTo(Completed).Finalize())
                .If(ctx => ctx.Saga.BankTransferSucceeded == false,
                    x => x.TransitionTo(Failed).Finalize())
                .If(ctx => ctx.Saga.BankTransferSucceeded == null,
                    x => x.TransitionTo(Processing)),
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
                        transfer.FailWorkflowStep(WorkflowStep.Names.AwaitingConfirmation, "Rejected by user");
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
                        transfer.FailWorkflowStep(WorkflowStep.Names.AwaitingConfirmation, "Timed out after 24 hours");
                        transfer.MarkFailed("Transfer auto-cancelled: balance change not accepted within 24 hours.");
                        await repo.UpdateAsync(transfer);
                        await publisher.PublishAsync(transfer);
                    }
                })
                .Finalize()
        );

        // ── Processing: only retry events arrive here (success/fail handled inline) ──
        During(Processing,
            When(RetryBankTransferEvent)
                .ThenAsync(async ctx =>
                {
                    await ExecuteBankTransferInline(ctx);
                })
                .If(ctx => ctx.Saga.BankTransferSucceeded == true,
                    x => x.TransitionTo(Completed).Finalize())
                .If(ctx => ctx.Saga.BankTransferSucceeded == false,
                    x => x.TransitionTo(Failed).Finalize())
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
            try
            {
                await repo.AddAsync(transfer);
                await publisher.PublishAsync(transfer);
            }
            catch (DuplicateInProgressTransferException)
            {
                transfer.MarkFailed("A transfer is already in progress for this employee.");
                await publisher.PublishAsync(transfer);
                ctx.Saga.BankTransferSucceeded = false;
                return;
            }
        }
        else if (transfer.Status != Domain.Enums.TransferStatus.Processing)
        {
            transfer.MarkProcessing();
            await repo.UpdateAsync(transfer);
            await publisher.PublishAsync(transfer);
        }

        var result = await bankService.ExecuteTransferAsync(
            ctx.Saga.CorrelationId, ctx.Saga.Amount, ctx.Saga.BankAccountId);

        transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);

        if (result.Success)
        {
            ctx.Saga.ExternalReferenceId = result.ExternalReferenceId;
            if (transfer != null)
            {
                transfer.CompleteWorkflowStep(WorkflowStep.Names.BankTransfer);
                transfer.CompleteWorkflowStep(WorkflowStep.Names.Complete);
                transfer.MarkCompleted(result.ExternalReferenceId!);
                await repo.UpdateAsync(transfer);
                await publisher.PublishAsync(transfer);
            }
            ctx.Saga.BankTransferSucceeded = true;
        }
        else
        {
            ctx.Saga.RetryCount++;
            if (ctx.Saga.RetryCount >= 3)
            {
                var reason = result.ErrorMessage ?? "Bank transfer failed after all retries.";
                ctx.Saga.FailureReason = reason;
                if (transfer != null)
                {
                    transfer.FailWorkflowStep(WorkflowStep.Names.BankTransfer, reason);
                    transfer.FailWorkflowStep(WorkflowStep.Names.Complete, "Transfer failed");
                    transfer.MarkFailed(reason);
                    await repo.UpdateAsync(transfer);
                    await publisher.PublishAsync(transfer);
                }
                ctx.Saga.BankTransferSucceeded = false;
            }
            else
            {
                // Schedule retry — this is an external event, not self-publish
                if (transfer != null)
                {
                    transfer.IncrementWorkflowStepRetry(WorkflowStep.Names.BankTransfer);
                    await repo.UpdateAsync(transfer);
                    await publisher.PublishAsync(transfer);
                }
                var delay = TimeSpan.FromSeconds(Math.Pow(2, ctx.Saga.RetryCount));
                await ctx.SchedulePublish(delay, new RetryBankTransfer(ctx.Saga.CorrelationId));
                ctx.Saga.BankTransferSucceeded = null; // pending retry
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
