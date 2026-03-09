using PayrollService.Domain.Common;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Events;

namespace PayrollService.Domain.Entities;

public class BankAccount : Entity
{
    public Guid EmployeeId { get; private set; }
    public string BankName { get; private set; } = string.Empty;
    public string AccountNumberMasked { get; private set; } = string.Empty;
    public string RoutingNumber { get; private set; } = string.Empty;
    public BankAccountType AccountType { get; private set; }
    public bool IsActive { get; private set; } = true;

    private BankAccount() { }

    public static BankAccount Create(
        Guid employeeId,
        string bankName,
        string accountNumberMasked,
        string routingNumber,
        BankAccountType accountType)
    {
        var account = new BankAccount
        {
            EmployeeId = employeeId,
            BankName = bankName,
            AccountNumberMasked = accountNumberMasked,
            RoutingNumber = routingNumber,
            AccountType = accountType,
            IsActive = true
        };

        account.AddDomainEvent(new BankAccountCreatedEvent(
            account.Id, employeeId, bankName, accountNumberMasked, accountType));
        return account;
    }

    public void Update(string bankName, string accountNumberMasked, string routingNumber, BankAccountType accountType)
    {
        BankName = bankName;
        AccountNumberMasked = accountNumberMasked;
        RoutingNumber = routingNumber;
        AccountType = accountType;
        SetUpdated();
        AddDomainEvent(new BankAccountUpdatedEvent(Id, EmployeeId, bankName, accountNumberMasked, accountType));
    }

    public void Deactivate()
    {
        IsActive = false;
        SetUpdated();
        AddDomainEvent(new BankAccountDeactivatedEvent(Id, EmployeeId));
    }
}
