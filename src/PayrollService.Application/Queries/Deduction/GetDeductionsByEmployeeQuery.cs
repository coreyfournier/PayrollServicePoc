using MediatR;
using PayrollService.Application.DTOs;
using PayrollService.Domain.Repositories;

namespace PayrollService.Application.Queries.Deduction;

public record GetDeductionsByEmployeeQuery(Guid EmployeeId) : IRequest<IEnumerable<DeductionDto>>;

public class GetDeductionsByEmployeeQueryHandler : IRequestHandler<GetDeductionsByEmployeeQuery, IEnumerable<DeductionDto>>
{
    private readonly IDeductionRepository _repository;

    public GetDeductionsByEmployeeQueryHandler(IDeductionRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<DeductionDto>> Handle(GetDeductionsByEmployeeQuery request, CancellationToken cancellationToken)
    {
        var deductions = await _repository.GetByEmployeeIdAsync(request.EmployeeId, cancellationToken);

        return deductions.Select(d => new DeductionDto(
            d.Id,
            d.EmployeeId,
            d.DeductionType,
            d.Description,
            d.Amount,
            d.IsPercentage,
            d.IsActive,
            d.CreatedAt,
            d.UpdatedAt));
    }
}
