using MediatR;
using TransferService.Application.DTOs;
using TransferService.Domain.Repositories;

namespace TransferService.Application.Queries.Transfer;

public record GetRecentTransfersQuery(int Limit = 50, string? Status = null) : IRequest<IEnumerable<TransferDto>>;

public class GetRecentTransfersQueryHandler : IRequestHandler<GetRecentTransfersQuery, IEnumerable<TransferDto>>
{
    private readonly ITransferRepository _repository;

    public GetRecentTransfersQueryHandler(ITransferRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<TransferDto>> Handle(GetRecentTransfersQuery request, CancellationToken cancellationToken)
    {
        var transfers = await _repository.GetRecentAsync(request.Limit, request.Status, cancellationToken);
        return transfers.Select(t => new TransferDto(
            t.Id, t.EmployeeId, t.Amount, t.PayPeriodNumber,
            t.Status, t.BankAccountId, t.InitiatedAt, t.CompletedAt,
            t.FailureReason, t.ExternalReferenceId, t.CurrentBalance,
            t.CreatedAt, t.UpdatedAt));
    }
}
