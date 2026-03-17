using ListenerApi.Data.Entities;

namespace ListenerApi.Data.Services;

public class TransferStatusChange
{
    public EmployeeTransferStatus TransferStatus { get; set; } = null!;
    public DateTime Timestamp { get; set; }
}
