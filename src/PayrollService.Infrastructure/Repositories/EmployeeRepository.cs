using MongoDB.Driver;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Repositories;
using PayrollService.Infrastructure.Persistence;

namespace PayrollService.Infrastructure.Repositories;

public class EmployeeRepository : IEmployeeRepository
{
    private readonly MongoDbContext _context;

    public EmployeeRepository(MongoDbContext context)
    {
        _context = context;
    }

    public async Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Employees
            .Find(e => e.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<Employee>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Employees
            .Find(_ => true)
            .ToListAsync(cancellationToken);
    }

    public async Task<Employee> AddAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        await _context.Employees.InsertOneAsync(employee, cancellationToken: cancellationToken);
        return employee;
    }

    public async Task UpdateAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        await _context.Employees.ReplaceOneAsync(
            e => e.Id == employee.Id,
            employee,
            cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _context.Employees.DeleteOneAsync(e => e.Id == id, cancellationToken);
    }
}
