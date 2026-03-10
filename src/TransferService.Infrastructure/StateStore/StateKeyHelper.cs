namespace TransferService.Infrastructure.StateStore;

public static class StateKeyHelper
{
    public static string GetTransferKey(Guid id) => $"transfer-{id}";
    public static string GetBankAccountKey(Guid id) => $"bankaccount-{id}";
}
