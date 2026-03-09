namespace PayrollService.Application.Interfaces;

public interface ITransferWorkflowStarter
{
    Task StartTransferWorkflowAsync(Guid transferId, CancellationToken cancellationToken = default);
}
