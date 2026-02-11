using PayrollService.Domain.Enums;

namespace PayrollService.Infrastructure.Orleans.State;

[GenerateSerializer]
public class EmployeeState
{
    [Id(0)]
    public Guid Id { get; set; }

    [Id(1)]
    public string FirstName { get; set; } = string.Empty;

    [Id(2)]
    public string LastName { get; set; } = string.Empty;

    [Id(3)]
    public string Email { get; set; } = string.Empty;

    [Id(4)]
    public PayType PayType { get; set; }

    [Id(5)]
    public decimal PayRate { get; set; }

    [Id(6)]
    public DateTime HireDate { get; set; }

    [Id(7)]
    public bool IsActive { get; set; } = true;

    [Id(8)]
    public DateTime CreatedAt { get; set; }

    [Id(9)]
    public DateTime UpdatedAt { get; set; }

    [Id(10)]
    public bool IsInitialized { get; set; }
}
