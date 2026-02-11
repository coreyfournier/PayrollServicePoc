namespace PayrollService.Infrastructure.Orleans.State;

[GenerateSerializer]
public class TaxInformationState
{
    [Id(0)]
    public Guid Id { get; set; }

    [Id(1)]
    public Guid EmployeeId { get; set; }

    [Id(2)]
    public string FederalFilingStatus { get; set; } = string.Empty;

    [Id(3)]
    public int FederalAllowances { get; set; }

    [Id(4)]
    public decimal AdditionalFederalWithholding { get; set; }

    [Id(5)]
    public string State { get; set; } = string.Empty;

    [Id(6)]
    public string StateFilingStatus { get; set; } = string.Empty;

    [Id(7)]
    public int StateAllowances { get; set; }

    [Id(8)]
    public decimal AdditionalStateWithholding { get; set; }

    [Id(9)]
    public DateTime CreatedAt { get; set; }

    [Id(10)]
    public DateTime UpdatedAt { get; set; }

    [Id(11)]
    public bool IsInitialized { get; set; }
}
