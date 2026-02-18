using MediatR;
using PayrollService.Application.DTOs;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Queries.Employee;

public record GetEmployeeByIdQuery(Guid Id) : IRequest<EmployeeDto?>;

public class GetEmployeeByIdQueryHandler : IRequestHandler<GetEmployeeByIdQuery, EmployeeDto?>
{
    private readonly IEmployeeRepository _repository;

    public GetEmployeeByIdQueryHandler(IEmployeeRepository repository)
    {
        _repository = repository;
    }

    public async Task<EmployeeDto?> Handle(GetEmployeeByIdQuery request, CancellationToken cancellationToken)
    {
        var employee = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (employee == null)
            return null;

        return new EmployeeDto(
            employee.Id,
            employee.FirstName,
            employee.LastName,
            employee.Email,
            employee.PayType,
            employee.PayRate,
            employee.PayPeriodHours,
            employee.HireDate,
            employee.IsActive,
            employee.CreatedAt,
            employee.UpdatedAt);
    }
}
