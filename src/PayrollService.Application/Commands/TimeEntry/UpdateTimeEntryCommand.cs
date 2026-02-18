using MediatR;
using PayrollService.Application.DTOs;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Commands.TimeEntry;

public record UpdateTimeEntryCommand(
    Guid Id,
    DateTime ClockIn,
    DateTime? ClockOut) : IRequest<TimeEntryDto>;

public class UpdateTimeEntryCommandHandler : IRequestHandler<UpdateTimeEntryCommand, TimeEntryDto>
{
    private readonly ITimeEntryRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateTimeEntryCommandHandler(ITimeEntryRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<TimeEntryDto> Handle(UpdateTimeEntryCommand request, CancellationToken cancellationToken)
    {
        var timeEntry = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Time entry with ID {request.Id} not found.");

        timeEntry.UpdateTimes(request.ClockIn, request.ClockOut);

        await _unitOfWork.ExecuteAsync(
            async () => await _repository.UpdateAsync(timeEntry, cancellationToken),
            timeEntry,
            cancellationToken);

        return new TimeEntryDto(
            timeEntry.Id,
            timeEntry.EmployeeId,
            timeEntry.ClockIn,
            timeEntry.ClockOut,
            timeEntry.HoursWorked,
            timeEntry.CreatedAt,
            timeEntry.UpdatedAt);
    }
}
