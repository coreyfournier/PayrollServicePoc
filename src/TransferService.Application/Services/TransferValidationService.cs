using Microsoft.Extensions.Options;
using TransferService.Application.Interfaces;
using TransferService.Application.Options;
using TransferService.Domain.Enums;
using TransferService.Domain.Repositories;
using TransferService.Domain.ValueObjects;

namespace TransferService.Application.Services;

public class TransferValidationService : ITransferValidationService
{
    private readonly ITransferRepository _transferRepository;
    private readonly IBankAccountRepository _bankAccountRepository;
    private readonly IEmployeeTransferLimitsRepository _limitsRepository;
    private readonly TransferLimitsOptions _options;

    public TransferValidationService(
        ITransferRepository transferRepository,
        IBankAccountRepository bankAccountRepository,
        IEmployeeTransferLimitsRepository limitsRepository,
        IOptions<TransferLimitsOptions> options)
    {
        _transferRepository = transferRepository;
        _bankAccountRepository = bankAccountRepository;
        _limitsRepository = limitsRepository;
        _options = options.Value;
    }

    public async Task<TransferValidationResult> ValidateAsync(TransferValidationRequest request, CancellationToken cancellationToken = default)
    {
        var reasons = new List<string>();

        // In-progress transfer check
        var hasInProgress = await _transferRepository.HasInProgressTransferAsync(request.EmployeeId, request.TransferId, cancellationToken);
        if (hasInProgress)
        {
            reasons.Add("A transfer is already in progress for this employee.");
            return new TransferValidationResult(false, reasons);
        }

        // Bank account validation
        var bankAccount = await _bankAccountRepository.GetByIdAsync(request.BankAccountId, cancellationToken);
        if (bankAccount == null || !bankAccount.IsActive)
        {
            reasons.Add("Bank account not found or inactive.");
            return new TransferValidationResult(false, reasons);
        }

        if (bankAccount.EmployeeId != request.EmployeeId)
        {
            reasons.Add("Bank account does not belong to this employee.");
            return new TransferValidationResult(false, reasons);
        }

        // Transfer limits validation
        var periodTransfers = await _transferRepository.GetByEmployeeAndPayPeriodAsync(
            request.EmployeeId, request.PayPeriodNumber, cancellationToken);
        var activeTransfers = periodTransfers.Where(t => t.Status != TransferStatus.Failed).ToList();

        var currentCount = activeTransfers.Count;
        var currentAmount = activeTransfers.Sum(t => t.Amount);

        var transfersToday = await _transferRepository.GetCountByEmployeeAndDateAsync(
            request.EmployeeId, DateTime.UtcNow.Date, cancellationToken);

        var employeeOverride = await _limitsRepository.GetByEmployeeIdAsync(request.EmployeeId);
        var limits = employeeOverride != null
            ? TransferLimits.FromEmployeeOverride(employeeOverride)
            : new TransferLimits(_options.MaxPerPayPeriod, _options.MaxAmountPerPayPeriod, _options.MaxPerDay);

        var validation = limits.Validate(currentCount, currentAmount, request.Amount, transfersToday);

        return new TransferValidationResult(validation.CanTransfer, validation.Reasons);
    }
}
