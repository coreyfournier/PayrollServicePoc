namespace PayrollService.Infrastructure.StateStore;

public static class StateKeyHelper
{
    private const char KeySeparator = '-';

    public static string GetKey(string entityType, Guid entityId)
    {
        return $"{entityType}{KeySeparator}{entityId}";
    }

    public static string GetEmployeeKey(Guid id) => GetKey("employee", id);
    public static string GetTimeEntryKey(Guid id) => GetKey("timeentry", id);
    public static string GetTaxInformationKey(Guid id) => GetKey("taxinformation", id);
    public static string GetDeductionKey(Guid id) => GetKey("deduction", id);

    public static (string EntityType, Guid EntityId) ParseKey(string key)
    {
        var separatorIndex = key.IndexOf(KeySeparator);
        if (separatorIndex == -1)
        {
            throw new ArgumentException($"Invalid state key format: {key}", nameof(key));
        }

        var entityType = key[..separatorIndex];
        var entityId = Guid.Parse(key[(separatorIndex + 1)..]);

        return (entityType, entityId);
    }
}
