using MediatR;
using PayrollService.Application.DTOs;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Commands.TimeEntry;

public record ClockOutCommand(Guid EmployeeId) : IRequest<TimeEntryDto>;

public class ClockOutCommandHandler : IRequestHandler<ClockOutCommand, TimeEntryDto>
{
    private readonly ITimeEntryRepository _timeEntryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ClockOutCommandHandler(ITimeEntryRepository timeEntryRepository, IUnitOfWork unitOfWork)
    {
        _timeEntryRepository = timeEntryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<TimeEntryDto> Handle(ClockOutCommand request, CancellationToken cancellationToken)
    {
        var activeEntry = await _timeEntryRepository.GetActiveEntryByEmployeeIdAsync(request.EmployeeId, cancellationToken)
            ?? throw new InvalidOperationException("Employee is not clocked in.");

        activeEntry.ClockOutEmployee();

        await _unitOfWork.ExecuteAsync(
            async () => await _timeEntryRepository.UpdateAsync(activeEntry, cancellationToken),
            activeEntry,
            cancellationToken);

        return new TimeEntryDto(
            activeEntry.Id,
            activeEntry.EmployeeId,
            activeEntry.ClockIn,
            activeEntry.ClockOut,
            activeEntry.HoursWorked,
            activeEntry.CreatedAt,
            activeEntry.UpdatedAt);
    }
}
