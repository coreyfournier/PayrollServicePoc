using MediatR;
using PayrollService.Application.DTOs;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Commands.Employee;

public record CreateEmployeeCommand(
    string FirstName,
    string LastName,
    string Email,
    PayType PayType,
    decimal PayRate,
    DateTime HireDate) : IRequest<EmployeeDto>;

public class CreateEmployeeCommandHandler : IRequestHandler<CreateEmployeeCommand, EmployeeDto>
{
    private readonly IEmployeeRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateEmployeeCommandHandler(IEmployeeRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<EmployeeDto> Handle(CreateEmployeeCommand request, CancellationToken cancellationToken)
    {
        var employee = Domain.Entities.Employee.Create(
            request.FirstName,
            request.LastName,
            request.Email,
            request.PayType,
            request.PayRate,
            request.HireDate);

        var result = await _unitOfWork.ExecuteAsync(
            async () => await _repository.AddAsync(employee, cancellationToken),
            employee,
            cancellationToken);

        return new EmployeeDto(
            result.Id,
            result.FirstName,
            result.LastName,
            result.Email,
            result.PayType,
            result.PayRate,
            result.HireDate,
            result.IsActive,
            result.CreatedAt,
            result.UpdatedAt);
    }
}
