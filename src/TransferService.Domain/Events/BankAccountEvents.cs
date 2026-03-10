using TransferService.Domain.Common;
using TransferService.Domain.Enums;

namespace TransferService.Domain.Events;

public class BankAccountCreatedEvent : DomainEvent
{
    public override string EventType => "bankaccount.created";
    public Guid BankAccountId { get; }
    public Guid EmployeeId { get; }
    public string BankName { get; }
    public string AccountNumberMasked { get; }
    public BankAccountType AccountType { get; }

    public BankAccountCreatedEvent(Guid bankAccountId, Guid employeeId, string bankName, string accountNumberMasked, BankAccountType accountType)
    {
        BankAccountId = bankAccountId;
        EmployeeId = employeeId;
        BankName = bankName;
        AccountNumberMasked = accountNumberMasked;
        AccountType = accountType;
    }
}

public class BankAccountUpdatedEvent : DomainEvent
{
    public override string EventType => "bankaccount.updated";
    public Guid BankAccountId { get; }
    public Guid EmployeeId { get; }
    public string BankName { get; }
    public string AccountNumberMasked { get; }
    public BankAccountType AccountType { get; }

    public BankAccountUpdatedEvent(Guid bankAccountId, Guid employeeId, string bankName, string accountNumberMasked, BankAccountType accountType)
    {
        BankAccountId = bankAccountId;
        EmployeeId = employeeId;
        BankName = bankName;
        AccountNumberMasked = accountNumberMasked;
        AccountType = accountType;
    }
}

public class BankAccountDeactivatedEvent : DomainEvent
{
    public override string EventType => "bankaccount.deactivated";
    public Guid BankAccountId { get; }
    public Guid EmployeeId { get; }

    public BankAccountDeactivatedEvent(Guid bankAccountId, Guid employeeId)
    {
        BankAccountId = bankAccountId;
        EmployeeId = employeeId;
    }
}
