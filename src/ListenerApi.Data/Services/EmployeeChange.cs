using ListenerApi.Data.Entities;

namespace ListenerApi.Data.Services;

public class EmployeeChange
{
    public EmployeeRecord Employee { get; set; } = null!;
    public string ChangeType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
