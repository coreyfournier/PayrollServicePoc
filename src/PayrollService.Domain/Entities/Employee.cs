using PayrollService.Domain.Common;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Events;

namespace PayrollService.Domain.Entities;

public class Employee : Entity
{
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public PayType PayType { get; private set; }
    public decimal PayRate { get; private set; }
    public decimal PayPeriodHours { get; private set; } = 40;
    public DateTime HireDate { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Employee() { }

    public static Employee Create(
        string firstName,
        string lastName,
        string email,
        PayType payType,
        decimal payRate,
        DateTime hireDate,
        decimal payPeriodHours = 40)
    {
        var employee = new Employee
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            PayType = payType,
            PayRate = payRate,
            PayPeriodHours = payPeriodHours,
            HireDate = hireDate,
            IsActive = true
        };

        employee.AddDomainEvent(new EmployeeCreatedEvent(employee.Id, firstName, lastName, email));
        return employee;
    }

    public void Update(string firstName, string lastName, string email, PayType payType, decimal payRate, decimal payPeriodHours = 40)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        PayType = payType;
        PayRate = payRate;
        PayPeriodHours = payPeriodHours;
        SetUpdated();

        AddDomainEvent(new EmployeeUpdatedEvent(Id, firstName, lastName, email, payType, payRate, payPeriodHours));
    }

    public void Deactivate()
    {
        IsActive = false;
        SetUpdated();
        AddDomainEvent(new EmployeeDeactivatedEvent(Id));
    }

    public void Activate()
    {
        IsActive = true;
        SetUpdated();
        AddDomainEvent(new EmployeeActivatedEvent(Id));
    }
}
