namespace TransferService.Domain.Exceptions;

public class DuplicateInProgressTransferException : Exception
{
    public Guid EmployeeId { get; }

    public DuplicateInProgressTransferException(Guid employeeId)
        : base($"Employee {employeeId} already has an in-progress transfer.")
    {
        EmployeeId = employeeId;
    }
}
