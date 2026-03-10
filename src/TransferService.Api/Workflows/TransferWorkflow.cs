using Dapr.Workflow;
using TransferService.Api.Workflows.Activities;

namespace TransferService.Api.Workflows;

public record TransferWorkflowInput(Guid TransferId, Guid EmployeeId, decimal Amount, long PayPeriodNumber, Guid BankAccountId);
public record TransferWorkflowResult(bool Success, string? ExternalReferenceId, string? FailureReason);

public class TransferWorkflow : Workflow<TransferWorkflowInput, TransferWorkflowResult>
{
    public override async Task<TransferWorkflowResult> RunAsync(WorkflowContext context, TransferWorkflowInput input)
    {
        var validationResult = await context.CallActivityAsync<ValidateTransferResult>(
            nameof(ValidateTransferActivity),
            new ValidateTransferInput(input.TransferId, input.EmployeeId, input.Amount, input.PayPeriodNumber));

        if (!validationResult.IsValid)
        {
            await context.CallActivityAsync(
                nameof(FailTransferActivity),
                new FailTransferInput(input.TransferId, validationResult.Reason!));
            return new TransferWorkflowResult(false, null, validationResult.Reason);
        }

        var balanceCheck = await context.CallActivityAsync<VerifyBalanceResult>(
            nameof(VerifyBalanceActivity),
            new VerifyBalanceInput(input.TransferId, input.EmployeeId, input.Amount, input.PayPeriodNumber));

        if (!balanceCheck.SufficientBalance)
        {
            await context.CallActivityAsync(
                nameof(MarkAwaitingConfirmationActivity),
                new MarkAwaitingConfirmationInput(input.TransferId, balanceCheck.CurrentBalance));

            bool accepted;
            try
            {
                accepted = await context.WaitForExternalEventAsync<bool>(
                    "BalanceAccepted",
                    TimeSpan.FromHours(24));
            }
            catch (TaskCanceledException)
            {
                accepted = false;
            }

            if (!accepted)
            {
                await context.CallActivityAsync(
                    nameof(FailTransferActivity),
                    new FailTransferInput(input.TransferId,
                        "Transfer auto-cancelled: balance change not accepted within 24 hours."));
                return new TransferWorkflowResult(false, null,
                    "Transfer auto-cancelled: balance change not accepted within 24 hours.");
            }
        }

        await context.CallActivityAsync(
            nameof(UpdateTransferStatusActivity),
            new UpdateTransferStatusInput(input.TransferId));

        var maxRetries = 3;
        BankTransferActivityResult? bankResult = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            bankResult = await context.CallActivityAsync<BankTransferActivityResult>(
                nameof(ExecuteBankTransferActivity),
                new ExecuteBankTransferInput(input.TransferId, input.Amount, input.BankAccountId));

            if (bankResult.Success)
                break;

            if (attempt < maxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await context.CreateTimer(delay);
            }
        }

        if (bankResult?.Success == true)
        {
            await context.CallActivityAsync(
                nameof(CompleteTransferActivity),
                new CompleteTransferInput(input.TransferId, bankResult.ExternalReferenceId!));
            return new TransferWorkflowResult(true, bankResult.ExternalReferenceId, null);
        }
        else
        {
            var reason = bankResult?.ErrorMessage ?? "Bank transfer failed after all retries.";
            await context.CallActivityAsync(
                nameof(FailTransferActivity),
                new FailTransferInput(input.TransferId, reason));
            return new TransferWorkflowResult(false, null, reason);
        }
    }
}
