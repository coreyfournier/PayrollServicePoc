using PayrollService.Domain.Common;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Events;

namespace PayrollService.Domain.Entities;

public class Deduction : Entity
{
    public Guid EmployeeId { get; private set; }
    public DeductionType DeductionType { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public bool IsPercentage { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Deduction() { }

    public static Deduction Create(
        Guid employeeId,
        DeductionType deductionType,
        string description,
        decimal amount,
        bool isPercentage)
    {
        var deduction = new Deduction
        {
            EmployeeId = employeeId,
            DeductionType = deductionType,
            Description = description,
            Amount = amount,
            IsPercentage = isPercentage,
            IsActive = true
        };

        deduction.AddDomainEvent(new DeductionCreatedEvent(deduction.Id, employeeId, deductionType, amount));
        return deduction;
    }

    public void Update(DeductionType deductionType, string description, decimal amount, bool isPercentage)
    {
        DeductionType = deductionType;
        Description = description;
        Amount = amount;
        IsPercentage = isPercentage;
        SetUpdated();

        AddDomainEvent(new DeductionUpdatedEvent(Id, EmployeeId, deductionType, amount));
    }

    public void Deactivate()
    {
        IsActive = false;
        SetUpdated();
        AddDomainEvent(new DeductionDeactivatedEvent(Id, EmployeeId));
    }
}
