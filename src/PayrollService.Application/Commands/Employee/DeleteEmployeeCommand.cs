using MediatR;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Commands.Employee;

public record DeleteEmployeeCommand(Guid Id) : IRequest<bool>;

public class DeleteEmployeeCommandHandler : IRequestHandler<DeleteEmployeeCommand, bool>
{
    private readonly IEmployeeRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteEmployeeCommandHandler(IEmployeeRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> Handle(DeleteEmployeeCommand request, CancellationToken cancellationToken)
    {
        var employee = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Employee with ID {request.Id} not found.");

        employee.Deactivate();

        await _unitOfWork.ExecuteAsync(
            async () => await _repository.UpdateAsync(employee, cancellationToken),
            employee,
            cancellationToken);

        return true;
    }
}
