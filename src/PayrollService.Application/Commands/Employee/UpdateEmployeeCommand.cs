using MediatR;
using PayrollService.Application.DTOs;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Commands.Employee;

public record UpdateEmployeeCommand(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    PayType PayType,
    decimal PayRate,
    decimal PayPeriodHours = 40) : IRequest<EmployeeDto>;

public class UpdateEmployeeCommandHandler : IRequestHandler<UpdateEmployeeCommand, EmployeeDto>
{
    private readonly IEmployeeRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateEmployeeCommandHandler(IEmployeeRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<EmployeeDto> Handle(UpdateEmployeeCommand request, CancellationToken cancellationToken)
    {
        var employee = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Employee with ID {request.Id} not found.");

        employee.Update(request.FirstName, request.LastName, request.Email, request.PayType, request.PayRate, request.PayPeriodHours);

        await _unitOfWork.ExecuteAsync(
            async () => await _repository.UpdateAsync(employee, cancellationToken),
            employee,
            cancellationToken);

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
