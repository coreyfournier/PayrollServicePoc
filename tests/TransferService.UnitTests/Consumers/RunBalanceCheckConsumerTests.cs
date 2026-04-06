using MassTransit;
using Microsoft.Extensions.Logging;
using TransferService.Api.Consumers;
using TransferService.Application.Interfaces;
using TransferService.Application.Messages;

namespace TransferService.UnitTests.Consumers;

public class RunBalanceCheckConsumerTests
{
    private readonly IBalanceService _balanceService = Substitute.For<IBalanceService>();
    private readonly ILogger<RunBalanceCheckConsumer> _logger = Substitute.For<ILogger<RunBalanceCheckConsumer>>();
    private readonly ConsumeContext<RunBalanceCheck> _context = Substitute.For<ConsumeContext<RunBalanceCheck>>();

    private readonly Guid _transferId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private const long PayPeriod = 59;

    private RunBalanceCheckConsumer CreateConsumer() => new(_balanceService, _logger);

    [Fact]
    public async Task NullBalance_ShouldPublishInsufficient()
    {
        // Arrange — ksqlDB returns null (query failure or no data)
        const decimal amount = 500m;
        _context.Message.Returns(new RunBalanceCheck(_transferId, _employeeId, amount, PayPeriod));
        _balanceService.GetCurrentBalanceAsync(_employeeId, PayPeriod).Returns((BalanceInfo?)null);

        // Act
        var consumer = CreateConsumer();
        await consumer.Consume(_context);

        // Assert — should treat null balance as insufficient
        await _context.Received(1).Publish(Arg.Is<BalanceCheckCompleted>(r =>
            r.TransferId == _transferId &&
            r.Sufficient == false &&
            r.CurrentBalance == 0));
    }

    [Fact]
    public async Task SufficientBalance_ShouldPublishSufficient()
    {
        // Arrange — net pay $1000, no transfers, requesting $500
        const decimal amount = 500m;
        _context.Message.Returns(new RunBalanceCheck(_transferId, _employeeId, amount, PayPeriod));
        _balanceService.GetCurrentBalanceAsync(_employeeId, PayPeriod)
            .Returns(new BalanceInfo(1000m, 0m, PayPeriod));

        // Act
        var consumer = CreateConsumer();
        await consumer.Consume(_context);

        // Assert
        await _context.Received(1).Publish(Arg.Is<BalanceCheckCompleted>(r =>
            r.TransferId == _transferId &&
            r.Sufficient == true &&
            r.CurrentBalance == 1000m));
    }

    [Fact]
    public async Task InsufficientBalance_ShouldPublishInsufficient()
    {
        // Arrange — net pay $400, no transfers, requesting $500
        const decimal amount = 500m;
        _context.Message.Returns(new RunBalanceCheck(_transferId, _employeeId, amount, PayPeriod));
        _balanceService.GetCurrentBalanceAsync(_employeeId, PayPeriod)
            .Returns(new BalanceInfo(400m, 0m, PayPeriod));

        // Act
        var consumer = CreateConsumer();
        await consumer.Consume(_context);

        // Assert
        await _context.Received(1).Publish(Arg.Is<BalanceCheckCompleted>(r =>
            r.TransferId == _transferId &&
            r.Sufficient == false &&
            r.CurrentBalance == 400m));
    }

    [Fact]
    public async Task BalanceReducedByPriorTransfers_ShouldBeInsufficient()
    {
        // Arrange — net pay $1000, $600 already transferred, requesting $500
        const decimal amount = 500m;
        _context.Message.Returns(new RunBalanceCheck(_transferId, _employeeId, amount, PayPeriod));
        _balanceService.GetCurrentBalanceAsync(_employeeId, PayPeriod)
            .Returns(new BalanceInfo(1000m, 600m, PayPeriod));

        // Act
        var consumer = CreateConsumer();
        await consumer.Consume(_context);

        // Assert — available is $400 which is less than $500
        await _context.Received(1).Publish(Arg.Is<BalanceCheckCompleted>(r =>
            r.TransferId == _transferId &&
            r.Sufficient == false &&
            r.CurrentBalance == 400m));
    }
}
