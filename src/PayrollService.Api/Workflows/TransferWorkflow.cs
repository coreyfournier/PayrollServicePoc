using Dapr.Workflow;
using PayrollService.Api.Workflows.Activities;

namespace PayrollService.Api.Workflows;

public record TransferWorkflowInput(Guid TransferId, Guid EmployeeId, decimal Amount, long PayPeriodNumber, Guid BankAccountId);
public record TransferWorkflowResult(bool Success, string? ExternalReferenceId, string? FailureReason);

public class TransferWorkflow : Workflow<TransferWorkflowInput, TransferWorkflowResult>
{
    public override async Task<TransferWorkflowResult> RunAsync(WorkflowContext context, TransferWorkflowInput input)
    {
        // Step 1: Validate transfer limits (re-check inside workflow as a race guard)
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

        // Step 2: Verify the balance is still sufficient for the transfer amount
        var balanceCheck = await context.CallActivityAsync<VerifyBalanceResult>(
            nameof(VerifyBalanceActivity),
            new VerifyBalanceInput(input.TransferId, input.EmployeeId, input.Amount, input.PayPeriodNumber));

        if (!balanceCheck.SufficientBalance)
        {
            // Balance has decreased — notify client and wait for confirmation
            await context.CallActivityAsync(
                nameof(MarkAwaitingConfirmationActivity),
                new MarkAwaitingConfirmationInput(input.TransferId, balanceCheck.CurrentBalance));

            // Wait up to 24 hours for client to accept the balance change
            bool accepted;
            try
            {
                accepted = await context.WaitForExternalEventAsync<bool>(
                    "BalanceAccepted",
                    TimeSpan.FromHours(24));
            }
            catch (TaskCanceledException)
            {
                // Timeout — auto-cancel
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

            // Client accepted — continue with the transfer
        }

        // Step 3: Mark transfer as processing
        await context.CallActivityAsync(
            nameof(UpdateTransferStatusActivity),
            new UpdateTransferStatusInput(input.TransferId));

        // Step 4: Execute bank transfer with retries
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

        // Step 5: Complete or fail based on bank result
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
