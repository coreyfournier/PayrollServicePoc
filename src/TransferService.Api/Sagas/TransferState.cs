using MassTransit;
using MongoDB.Bson.Serialization.Attributes;

namespace TransferService.Api.Sagas;

public class TransferState : SagaStateMachineInstance, ISagaVersion
{
    [BsonId]
    public Guid CorrelationId { get; set; } // = TransferId
    public string CurrentState { get; set; } = default!;
    public int Version { get; set; }

    public Guid EmployeeId { get; set; }
    public decimal Amount { get; set; }
    public long PayPeriodNumber { get; set; }
    public Guid BankAccountId { get; set; }
    public decimal? CurrentBalance { get; set; }
    public string? ExternalReferenceId { get; set; }
    public string? FailureReason { get; set; }
    public int RetryCount { get; set; }
    public Guid? ConfirmationTimeoutTokenId { get; set; }

    /// <summary>
    /// Used for inline branching during message processing.
    /// </summary>
    public TransferOutcome TransferOutcome { get; set; }
    public string? OutcomeDetail { get; set; }
    public bool? BankTransferSucceeded { get; set; }
}

public enum TransferOutcome
{
    Pending = 0,
    ValidationFailed = 1,
    BalanceSufficient = 2,
    BalanceInsufficient = 3
}
