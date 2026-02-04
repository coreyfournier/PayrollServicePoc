using MediatR;
using PayrollService.Application.DTOs;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Queries.TimeEntry;

public record GetTimeEntriesByEmployeeQuery(Guid EmployeeId) : IRequest<IEnumerable<TimeEntryDto>>;

public class GetTimeEntriesByEmployeeQueryHandler : IRequestHandler<GetTimeEntriesByEmployeeQuery, IEnumerable<TimeEntryDto>>
{
    private readonly ITimeEntryRepository _repository;

    public GetTimeEntriesByEmployeeQueryHandler(ITimeEntryRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<TimeEntryDto>> Handle(GetTimeEntriesByEmployeeQuery request, CancellationToken cancellationToken)
    {
        var entries = await _repository.GetByEmployeeIdAsync(request.EmployeeId, cancellationToken);

        return entries.Select(e => new TimeEntryDto(
            e.Id,
            e.EmployeeId,
            e.ClockIn,
            e.ClockOut,
            e.HoursWorked,
            e.CreatedAt,
            e.UpdatedAt));
    }
}
