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
    // States
    public State CheckingBalance { get; private set; } = default!;
    public State AwaitingConfirmation { get; private set; } = default!;
    public State CheckingFraud { get; private set; } = default!;
    public State Transferring { get; private set; } = default!;
    public State Completed { get; private set; } = default!;
    public State Failed { get; private set; } = default!;

    // Events (all delivered via RabbitMQ with at-least-once delivery)
    public Event<TransferRequested> InitiateTransfer { get; private set; } = default!;
    public Event<BalanceCheckCompleted> BalanceCheckCompletedEvent { get; private set; } = default!;
    public Event<BalanceAccepted> AcceptBalance { get; private set; } = default!;
    public Event<FraudCheckCompleted> FraudCheckCompletedEvent { get; private set; } = default!;
    public Event<BankTransferCompleted> BankTransferCompletedEvent { get; private set; } = default!;

    // Schedules (RabbitMQ delayed delivery)
    public Schedule<TransferState, ConfirmationTimedOut> ConfirmationTimeout { get; private set; } = default!;
    public Schedule<TransferState, RetryBankTransfer> BankTransferRetry { get; private set; } = default!;

    public TransferStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => InitiateTransfer, x => x.CorrelateById(ctx => ctx.Message.TransferId));
        Event(() => BalanceCheckCompletedEvent, x => x.CorrelateById(ctx => ctx.Message.TransferId));
        Event(() => AcceptBalance, x => x.CorrelateById(ctx => ctx.Message.TransferId));
        Event(() => FraudCheckCompletedEvent, x => x.CorrelateById(ctx => ctx.Message.TransferId));
        Event(() => BankTransferCompletedEvent, x => x.CorrelateById(ctx => ctx.Message.TransferId));

        Schedule(() => ConfirmationTimeout, instance => instance.ConfirmationTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromHours(24);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.TransferId);
        });

        Schedule(() => BankTransferRetry, instance => instance.RetryBankTransferTokenId, s =>
        {
            s.Delay = TimeSpan.FromSeconds(2);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.TransferId);
        });

        // ── Initial: create transfer, validate (with delay), then send RunBalanceCheck command ──
        Initially(
            When(InitiateTransfer)
                .Then(ctx =>
                {
                    ctx.Saga.EmployeeId = ctx.Message.EmployeeId;
                    ctx.Saga.Amount = ctx.Message.Amount;
                    ctx.Saga.PayPeriodNumber = ctx.Message.PayPeriodNumber;
                    ctx.Saga.BankAccountId = ctx.Message.BankAccountId;
                })
                // Step 1: Create transfer entity and notify bridge (Validation=InProgress)
                .ThenAsync(async ctx =>
                {
                    using var scope = CreateScope(ctx);
                    var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
                    var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                    var transfer = Transfer.Create(
                        ctx.Saga.EmployeeId, ctx.Saga.Amount, ctx.Saga.PayPeriodNumber,
                        ctx.Saga.BankAccountId, ctx.Saga.CorrelationId);
                    try
                    {
                        await repo.AddAsync(transfer);
                        await bus.Publish(new TransferUpdated(ctx.Saga.CorrelationId));
                    }
                    catch (DuplicateInProgressTransferException)
                    {
                        ctx.Saga.FailureReason = "A transfer is already in progress for this employee.";
                    }
                })
                // Step 2: Simulate validation delay
                .ThenAsync(async ctx =>
                {
                    if (ctx.Saga.FailureReason == null)
                        await Task.Delay(Random.Shared.Next(1000, 3000));
                })
                // Step 3: Run validation and notify bridge with result
                .ThenAsync(async ctx =>
                {
                    if (ctx.Saga.FailureReason != null) return;

                    using var scope = CreateScope(ctx);
                    var validationService = scope.ServiceProvider.GetRequiredService<ITransferValidationService>();
                    var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
                    var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                    var result = await validationService.ValidateAsync(
                        new TransferValidationRequest(
                            ctx.Saga.EmployeeId, ctx.Saga.Amount,
                            ctx.Saga.PayPeriodNumber, ctx.Saga.BankAccountId,
                            ctx.Saga.CorrelationId));

                    var transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);
                    if (transfer == null) return;

                    if (!result.CanTransfer)
                    {
                        ctx.Saga.FailureReason = string.Join(" ", result.Reasons);
                        transfer.FailWorkflowStep(WorkflowStep.Names.Validation, ctx.Saga.FailureReason);
                        transfer.MarkFailed(ctx.Saga.FailureReason);
                    }
                    else
                    {
                        transfer.CompleteWorkflowStep(WorkflowStep.Names.Validation);
                        transfer.StartWorkflowStep(WorkflowStep.Names.BalanceCheck);
                    }
                    await repo.UpdateAsync(transfer);
                    await bus.Publish(new TransferUpdated(ctx.Saga.CorrelationId));
                })
                .IfElse(ctx => ctx.Saga.FailureReason != null,
                    fail => fail
                        .TransitionTo(Failed)
                        .Finalize(),
                    pass => pass
                        .TransitionTo(CheckingBalance)
                        .PublishAsync(ctx => Task.FromResult(new RunBalanceCheck(
                            ctx.Saga.CorrelationId, ctx.Saga.EmployeeId,
                            ctx.Saga.Amount, ctx.Saga.PayPeriodNumber)))
                )
        );

        // ── CheckingBalance: result arrives from RunBalanceCheckConsumer ──
        During(CheckingBalance,
            When(BalanceCheckCompletedEvent, ctx => ctx.Message.Sufficient)
                .TransitionTo(CheckingFraud)
                .ThenAsync(async ctx =>
                {
                    ctx.Saga.CurrentBalance = ctx.Message.CurrentBalance;
                    using var scope = CreateScope(ctx);
                    var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
                    var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                    var transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);
                    if (transfer == null) return;

                    transfer.CompleteWorkflowStep(WorkflowStep.Names.BalanceCheck);
                    transfer.StartWorkflowStep(WorkflowStep.Names.FraudCheck);
                    transfer.MarkProcessing();
                    await repo.UpdateAsync(transfer);
                    await bus.Publish(new TransferUpdated(ctx.Saga.CorrelationId));
                })
                .PublishAsync(ctx => Task.FromResult(
                    new RunFraudCheck(ctx.Saga.CorrelationId))),
            When(BalanceCheckCompletedEvent, ctx => !ctx.Message.Sufficient)
                .TransitionTo(AwaitingConfirmation)
                .ThenAsync(async ctx =>
                {
                    ctx.Saga.CurrentBalance = ctx.Message.CurrentBalance;
                    using var scope = CreateScope(ctx);
                    var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
                    var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                    var transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);
                    if (transfer == null) return;

                    transfer.CompleteWorkflowStep(WorkflowStep.Names.BalanceCheck,
                        $"Balance ${ctx.Saga.CurrentBalance:F2} is less than transfer amount");
                    transfer.AddWorkflowStep(WorkflowStep.Names.AwaitingConfirmation, WorkflowStep.Statuses.InProgress);
                    transfer.MarkAwaitingConfirmation(ctx.Saga.CurrentBalance!.Value);
                    await repo.UpdateAsync(transfer);
                    await bus.Publish(new TransferUpdated(ctx.Saga.CorrelationId));
                })
                .Schedule(ConfirmationTimeout, ctx => ctx.Init<ConfirmationTimedOut>(new ConfirmationTimedOut(ctx.Saga.CorrelationId)))
        );

        // ── AwaitingConfirmation: user accepts, rejects, or timeout ──
        During(AwaitingConfirmation,
            When(AcceptBalance, ctx => ctx.Message.Accepted)
                .Unschedule(ConfirmationTimeout)
                .TransitionTo(CheckingFraud)
                .ThenAsync(async ctx =>
                {
                    using var scope = CreateScope(ctx);
                    var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
                    var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                    var transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);
                    if (transfer != null)
                    {
                        transfer.CompleteWorkflowStep(WorkflowStep.Names.AwaitingConfirmation, "Accepted by user");
                        transfer.StartWorkflowStep(WorkflowStep.Names.FraudCheck);
                        transfer.MarkProcessing();
                        await repo.UpdateAsync(transfer);
                        await bus.Publish(new TransferUpdated(ctx.Saga.CorrelationId));
                    }
                })
                .PublishAsync(ctx => Task.FromResult(new RunFraudCheck(ctx.Saga.CorrelationId))),
            When(AcceptBalance, ctx => !ctx.Message.Accepted)
                .Unschedule(ConfirmationTimeout)
                .TransitionTo(Failed)
                .ThenAsync(async ctx =>
                {
                    ctx.Saga.FailureReason = "Transfer rejected by user.";
                    using var scope = CreateScope(ctx);
                    var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
                    var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                    var transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);
                    if (transfer != null)
                    {
                        transfer.FailWorkflowStep(WorkflowStep.Names.AwaitingConfirmation, "Rejected by user");
                        transfer.MarkFailed("Transfer rejected by user.");
                        await repo.UpdateAsync(transfer);
                        await bus.Publish(new TransferUpdated(ctx.Saga.CorrelationId));
                    }
                })
                .Finalize(),
            When(ConfirmationTimeout.Received)
                .TransitionTo(Failed)
                .ThenAsync(async ctx =>
                {
                    ctx.Saga.FailureReason = "Transfer auto-cancelled: balance change not accepted within 24 hours.";
                    using var scope = CreateScope(ctx);
                    var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
                    var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                    var transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);
                    if (transfer != null)
                    {
                        transfer.FailWorkflowStep(WorkflowStep.Names.AwaitingConfirmation, "Timed out after 24 hours");
                        transfer.MarkFailed("Transfer auto-cancelled: balance change not accepted within 24 hours.");
                        await repo.UpdateAsync(transfer);
                        await bus.Publish(new TransferUpdated(ctx.Saga.CorrelationId));
                    }
                })
                .Finalize()
        );

        // ── CheckingFraud: result arrives from RunFraudCheckConsumer ──
        During(CheckingFraud,
            When(FraudCheckCompletedEvent)
                .TransitionTo(Transferring)
                .ThenAsync(async ctx =>
                {
                    using var scope = CreateScope(ctx);
                    var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
                    var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                    var transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);
                    if (transfer != null)
                    {
                        transfer.CompleteWorkflowStep(WorkflowStep.Names.FraudCheck);
                        transfer.StartWorkflowStep(WorkflowStep.Names.BankTransfer);
                        await repo.UpdateAsync(transfer);
                        await bus.Publish(new TransferUpdated(ctx.Saga.CorrelationId));
                    }
                })
                .PublishAsync(ctx => Task.FromResult(
                    new RunBankTransfer(ctx.Saga.CorrelationId, ctx.Saga.Amount, ctx.Saga.BankAccountId)))
        );

        // ── Transferring: result arrives from RunBankTransferConsumer ──
        During(Transferring,
            When(BankTransferCompletedEvent, ctx => ctx.Message.Success)
                .TransitionTo(Completed)
                .ThenAsync(async ctx =>
                {
                    ctx.Saga.ExternalReferenceId = ctx.Message.ExternalReferenceId;
                    using var scope = CreateScope(ctx);
                    var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
                    var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                    var transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);
                    if (transfer != null)
                    {
                        transfer.CompleteWorkflowStep(WorkflowStep.Names.BankTransfer);
                        transfer.CompleteWorkflowStep(WorkflowStep.Names.Complete);
                        transfer.MarkCompleted(ctx.Message.ExternalReferenceId!);
                        await repo.UpdateAsync(transfer);
                        await bus.Publish(new TransferUpdated(ctx.Saga.CorrelationId));
                    }
                })
                .Finalize(),
            When(BankTransferCompletedEvent, ctx => !ctx.Message.Success)
                .ThenAsync(async ctx =>
                {
                    ctx.Saga.RetryCount++;
                    using var scope = CreateScope(ctx);
                    var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
                    var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                    var transfer = await repo.GetByIdAsync(ctx.Saga.CorrelationId);

                    if (ctx.Saga.RetryCount >= 3)
                    {
                        var reason = ctx.Message.ErrorMessage ?? "Bank transfer failed after all retries.";
                        ctx.Saga.FailureReason = reason;
                        if (transfer != null)
                        {
                            transfer.FailWorkflowStep(WorkflowStep.Names.BankTransfer, reason);
                            transfer.FailWorkflowStep(WorkflowStep.Names.Complete, "Transfer failed");
                            transfer.MarkFailed(reason);
                            await repo.UpdateAsync(transfer);
                            await bus.Publish(new TransferUpdated(ctx.Saga.CorrelationId));
                        }
                    }
                    else
                    {
                        if (transfer != null)
                        {
                            transfer.IncrementWorkflowStepRetry(WorkflowStep.Names.BankTransfer);
                            await repo.UpdateAsync(transfer);
                            await bus.Publish(new TransferUpdated(ctx.Saga.CorrelationId));
                        }
                    }
                })
                .If(ctx => ctx.Saga.RetryCount >= 3,
                    x => x.TransitionTo(Failed).Finalize())
                .If(ctx => ctx.Saga.RetryCount < 3,
                    x => x.Schedule(BankTransferRetry, ctx =>
                        ctx.Init<RetryBankTransfer>(new RetryBankTransfer(ctx.Saga.CorrelationId)))),
            When(BankTransferRetry.Received)
                .PublishAsync(ctx => Task.FromResult(
                    new RunBankTransfer(ctx.Saga.CorrelationId, ctx.Saga.Amount, ctx.Saga.BankAccountId)))
        );

        SetCompletedWhenFinalized();
    }

    private static IServiceScope CreateScope<TSaga, TMessage>(BehaviorContext<TSaga, TMessage> ctx)
        where TSaga : class, SagaStateMachineInstance
        where TMessage : class
    {
        var provider = ctx.GetPayload<IServiceProvider>();
        return provider.CreateScope();
    }
}
