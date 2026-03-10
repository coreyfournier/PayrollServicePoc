using MediatR;
using TransferService.Application.DTOs;
using TransferService.Domain.Repositories;

namespace TransferService.Application.Queries.Transfer;

public record GetTransfersByEmployeeQuery(Guid EmployeeId) : IRequest<IEnumerable<TransferDto>>;

public class GetTransfersByEmployeeQueryHandler : IRequestHandler<GetTransfersByEmployeeQuery, IEnumerable<TransferDto>>
{
    private readonly ITransferRepository _repository;

    public GetTransfersByEmployeeQueryHandler(ITransferRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<TransferDto>> Handle(GetTransfersByEmployeeQuery request, CancellationToken cancellationToken)
    {
        var transfers = await _repository.GetByEmployeeIdAsync(request.EmployeeId, cancellationToken);
        return transfers.Select(t => new TransferDto(
            t.Id, t.EmployeeId, t.Amount, t.PayPeriodNumber,
            t.Status, t.BankAccountId, t.InitiatedAt, t.CompletedAt,
            t.FailureReason, t.ExternalReferenceId, t.CurrentBalance,
            t.CreatedAt, t.UpdatedAt));
    }
}
