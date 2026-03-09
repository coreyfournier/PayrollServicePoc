using ListenerApi.Data.Entities;

namespace ListenerApi.Data.Services;

public class TransferChange
{
    public TransferRecord Transfer { get; set; } = null!;
    public string ChangeType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
