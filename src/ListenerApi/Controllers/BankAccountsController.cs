using ListenerApi.Data.Entities;
using ListenerApi.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace ListenerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BankAccountsController : ControllerBase
{
    private readonly IBankAccountRepository _repository;

    public BankAccountsController(IBankAccountRepository repository)
    {
        _repository = repository;
    }

    [HttpGet("employee/{employeeId:guid}")]
    public async Task<ActionResult<List<BankAccount>>> GetByEmployee(Guid employeeId)
    {
        var accounts = await _repository.GetByEmployeeIdAsync(employeeId);
        return Ok(accounts);
    }

    [HttpPost]
    public async Task<ActionResult<BankAccount>> Create([FromBody] CreateBankAccountRequest request)
    {
        var now = DateTime.UtcNow;
        var bankAccount = new BankAccount
        {
            Id = Guid.NewGuid(),
            EmployeeId = request.EmployeeId,
            BankName = request.BankName,
            AccountNumberMasked = request.AccountNumberMasked,
            RoutingNumber = request.RoutingNumber,
            AccountType = request.AccountType,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _repository.AddAsync(bankAccount);
        return Created($"/api/bankaccounts/{bankAccount.Id}", bankAccount);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BankAccount>> Update(Guid id, [FromBody] UpdateBankAccountRequest request)
    {
        var bankAccount = await _repository.GetByIdAsync(id);
        if (bankAccount == null)
            return NotFound();

        bankAccount.BankName = request.BankName;
        bankAccount.AccountNumberMasked = request.AccountNumberMasked;
        bankAccount.RoutingNumber = request.RoutingNumber;
        bankAccount.AccountType = request.AccountType;
        bankAccount.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(bankAccount);
        return Ok(bankAccount);
    }
}

public record CreateBankAccountRequest(
    Guid EmployeeId,
    string BankName,
    string AccountNumberMasked,
    string RoutingNumber,
    int AccountType);

public record UpdateBankAccountRequest(
    string BankName,
    string AccountNumberMasked,
    string RoutingNumber,
    int AccountType);
