using MediatR;
using PayrollService.Application.DTOs;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Commands.TimeEntry;

public record CreateTimeEntryCommand(Guid EmployeeId, DateTime ClockIn, DateTime? ClockOut) : IRequest<TimeEntryDto>;

public class CreateTimeEntryCommandHandler : IRequestHandler<CreateTimeEntryCommand, TimeEntryDto>
{
    private readonly ITimeEntryRepository _timeEntryRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTimeEntryCommandHandler(
        ITimeEntryRepository timeEntryRepository,
        IEmployeeRepository employeeRepository,
        IUnitOfWork unitOfWork)
    {
        _timeEntryRepository = timeEntryRepository;
        _employeeRepository = employeeRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<TimeEntryDto> Handle(CreateTimeEntryCommand request, CancellationToken cancellationToken)
    {
        var employee = await _employeeRepository.GetByIdAsync(request.EmployeeId, cancellationToken)
            ?? throw new KeyNotFoundException($"Employee with ID {request.EmployeeId} not found.");

        var timeEntry = Domain.Entities.TimeEntry.Create(request.EmployeeId, request.ClockIn, request.ClockOut);

        var result = await _unitOfWork.ExecuteAsync(
            async () => await _timeEntryRepository.AddAsync(timeEntry, cancellationToken),
            timeEntry,
            cancellationToken);

        return new TimeEntryDto(
            result.Id,
            result.EmployeeId,
            result.ClockIn,
            result.ClockOut,
            result.HoursWorked,
            result.CreatedAt,
            result.UpdatedAt);
    }
}
