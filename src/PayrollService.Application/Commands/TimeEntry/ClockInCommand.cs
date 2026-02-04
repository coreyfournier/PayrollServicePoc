using MediatR;
using PayrollService.Application.DTOs;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Commands.TimeEntry;

public record ClockInCommand(Guid EmployeeId) : IRequest<TimeEntryDto>;

public class ClockInCommandHandler : IRequestHandler<ClockInCommand, TimeEntryDto>
{
    private readonly ITimeEntryRepository _timeEntryRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ClockInCommandHandler(
        ITimeEntryRepository timeEntryRepository,
        IEmployeeRepository employeeRepository,
        IUnitOfWork unitOfWork)
    {
        _timeEntryRepository = timeEntryRepository;
        _employeeRepository = employeeRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<TimeEntryDto> Handle(ClockInCommand request, CancellationToken cancellationToken)
    {
        var employee = await _employeeRepository.GetByIdAsync(request.EmployeeId, cancellationToken)
            ?? throw new KeyNotFoundException($"Employee with ID {request.EmployeeId} not found.");

        var activeEntry = await _timeEntryRepository.GetActiveEntryByEmployeeIdAsync(request.EmployeeId, cancellationToken);
        if (activeEntry != null)
            throw new InvalidOperationException("Employee is already clocked in.");

        var timeEntry = Domain.Entities.TimeEntry.ClockInEmployee(request.EmployeeId);

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
