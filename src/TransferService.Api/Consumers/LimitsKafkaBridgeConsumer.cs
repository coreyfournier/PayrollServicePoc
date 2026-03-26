using MassTransit;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using TransferService.Application.Interfaces;
using TransferService.Application.Messages;
using TransferService.Application.Options;
using TransferService.Domain.Repositories;

namespace TransferService.Api.Consumers;

public class LimitsKafkaBridgeConsumer : IConsumer<EmployeeLimitsUpdated>
{
    private readonly IEmployeeTransferLimitsRepository _limitsRepository;
    private readonly ILimitsEventPublisher _publisher;
    private readonly TransferLimitsOptions _options;
    private readonly ILogger<LimitsKafkaBridgeConsumer> _logger;

    public LimitsKafkaBridgeConsumer(
        IEmployeeTransferLimitsRepository limitsRepository,
        ILimitsEventPublisher publisher,
        IOptions<TransferLimitsOptions> options,
        ILogger<LimitsKafkaBridgeConsumer> logger)
    {
        _limitsRepository = limitsRepository;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<EmployeeLimitsUpdated> context)
    {
        var employeeId = context.Message.EmployeeId;

        var customLimits = await _limitsRepository.GetByEmployeeIdAsync(employeeId);

        var maxPerPayPeriod = customLimits?.MaxTransfersPerPayPeriod ?? _options.MaxPerPayPeriod;
        var maxAmountPerPayPeriod = customLimits?.MaxAmountPerPayPeriod ?? _options.MaxAmountPerPayPeriod;
        var maxPerDay = customLimits?.MaxTransfersPerDay ?? _options.MaxPerDay;

        await _publisher.PublishAsync(employeeId, maxPerPayPeriod, maxAmountPerPayPeriod, maxPerDay);

        _logger.LogInformation(
            "Bridged limits for employee {EmployeeId} to Kafka (maxPerPeriod={MaxPerPeriod}, maxAmount={MaxAmount}, maxPerDay={MaxPerDay})",
            employeeId, maxPerPayPeriod, maxAmountPerPayPeriod, maxPerDay);
    }
}
