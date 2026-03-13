using System.Runtime.Serialization;
using Dapr.Actors;

namespace TransferService.Api.Actors;

public interface ITransferActor : IActor
{
    Task<TransferActorResult> InitiateTransferAsync(TransferActorRequest request);
}

[DataContract]
public class TransferActorRequest
{
    [DataMember]
    public decimal Amount { get; set; }

    [DataMember]
    public long PayPeriodNumber { get; set; }

    [DataMember]
    public Guid BankAccountId { get; set; }

    [DataMember]
    public Guid? TransferId { get; set; }

    public TransferActorRequest() { }

    public TransferActorRequest(decimal amount, long payPeriodNumber, Guid bankAccountId, Guid? transferId = null)
    {
        Amount = amount;
        PayPeriodNumber = payPeriodNumber;
        BankAccountId = bankAccountId;
        TransferId = transferId;
    }
}

[DataContract]
public class TransferActorResult
{
    [DataMember]
    public bool Success { get; set; }

    [DataMember]
    public Guid? TransferId { get; set; }

    [DataMember]
    public string? ErrorMessage { get; set; }

    public TransferActorResult() { }

    public TransferActorResult(bool success, Guid? transferId, string? errorMessage)
    {
        Success = success;
        TransferId = transferId;
        ErrorMessage = errorMessage;
    }
}
