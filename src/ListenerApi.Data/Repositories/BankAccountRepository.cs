using ListenerApi.Data.DbContext;
using ListenerApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ListenerApi.Data.Repositories;

public class BankAccountRepository : IBankAccountRepository
{
    private readonly ListenerDbContext _context;

    public BankAccountRepository(ListenerDbContext context)
    {
        _context = context;
    }

    public async Task<BankAccount?> GetByIdAsync(Guid id)
        => await _context.BankAccounts.FindAsync(id);

    public async Task<List<BankAccount>> GetByEmployeeIdAsync(Guid employeeId)
        => await _context.BankAccounts
            .Where(b => b.EmployeeId == employeeId)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync();

    public async Task<BankAccount> AddAsync(BankAccount bankAccount)
    {
        _context.BankAccounts.Add(bankAccount);
        await _context.SaveChangesAsync();
        return bankAccount;
    }

    public async Task UpdateAsync(BankAccount bankAccount)
    {
        _context.BankAccounts.Update(bankAccount);
        await _context.SaveChangesAsync();
    }
}
