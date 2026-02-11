using PayrollService.Domain.Enums;

namespace PayrollService.Infrastructure.Orleans.State;

[GenerateSerializer]
public class DeductionState
{
    [Id(0)]
    public Guid Id { get; set; }

    [Id(1)]
    public Guid EmployeeId { get; set; }

    [Id(2)]
    public DeductionType DeductionType { get; set; }

    [Id(3)]
    public string Description { get; set; } = string.Empty;

    [Id(4)]
    public decimal Amount { get; set; }

    [Id(5)]
    public bool IsPercentage { get; set; }

    [Id(6)]
    public bool IsActive { get; set; } = true;

    [Id(7)]
    public DateTime CreatedAt { get; set; }

    [Id(8)]
    public DateTime UpdatedAt { get; set; }

    [Id(9)]
    public bool IsInitialized { get; set; }
}
