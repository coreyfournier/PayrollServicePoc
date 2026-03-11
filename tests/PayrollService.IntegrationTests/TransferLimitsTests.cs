using FluentAssertions;
using PayrollService.IntegrationTests.Infrastructure;

namespace PayrollService.IntegrationTests;

/// <summary>
/// Tests transfer limit enforcement: per-day, per-pay-period count,
/// and per-pay-period amount limits.
/// </summary>
[Collection("Integration")]
public class TransferLimitsTests
{
    private readonly TestFixture _fixture;

    public TransferLimitsTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Transfer_DailyLimitReached_RejectsSecondTransfer()
    {
        // Arrange — MaxPerDay is 1
        var employee = _fixture.GetEmployee("Emily");
        var bankAccount = _fixture.GetBankAccount(employee.Id);
        var payPeriod = TestFixture.CurrentPayPeriod;

        // First transfer should succeed
        var first = await _fixture.Api.InitiateTransferAsync(
            employee.Id, 10m, payPeriod, bankAccount.Id);
        first.Success.Should().BeTrue("first transfer of the day should be accepted");

        // Act — second transfer same day
        var second = await _fixture.Api.InitiateTransferAsync(
            employee.Id, 10m, payPeriod, bankAccount.Id);

        // Assert
        second.Success.Should().BeFalse("daily limit of 1 should block the second transfer");
        second.ErrorMessage.Should().Contain("Daily transfer limit");
    }

    [Fact]
    public async Task TransferLimits_ReflectCurrentUsage()
    {
        // Arrange
        var employee = _fixture.GetEmployee("Sarah");
        var bankAccount = _fixture.GetBankAccount(employee.Id);
        var payPeriod = TestFixture.CurrentPayPeriod;

        // Check limits before any transfer
        var limitsBefore = await _fixture.Api.GetTransferLimitsAsync(employee.Id, payPeriod);
        limitsBefore.CanTransfer.Should().BeTrue();
        limitsBefore.CurrentPeriodCount.Should().Be(0);
        limitsBefore.CurrentPeriodAmount.Should().Be(0);

        // Initiate a transfer
        var result = await _fixture.Api.InitiateTransferAsync(
            employee.Id, 50m, payPeriod, bankAccount.Id);
        result.Success.Should().BeTrue();

        // Check limits after transfer
        var limitsAfter = await _fixture.Api.GetTransferLimitsAsync(employee.Id, payPeriod);
        limitsAfter.CurrentPeriodCount.Should().BeGreaterThanOrEqualTo(1);
        limitsAfter.TransfersToday.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Transfer_InvalidBankAccount_Rejected()
    {
        var employee = _fixture.GetEmployee("John");
        var fakeBankAccountId = Guid.NewGuid();
        var payPeriod = TestFixture.CurrentPayPeriod;

        var result = await _fixture.Api.InitiateTransferAsync(
            employee.Id, 10m, payPeriod, fakeBankAccountId);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Bank account");
    }

    [Fact]
    public async Task Transfer_WrongEmployeeBankAccount_Rejected()
    {
        // Arrange — use John's bank account for Sarah's transfer
        var john = _fixture.GetEmployee("John");
        var sarah = _fixture.GetEmployee("Sarah");
        var johnBankAccount = _fixture.GetBankAccount(john.Id);
        var payPeriod = TestFixture.CurrentPayPeriod;

        var result = await _fixture.Api.InitiateTransferAsync(
            sarah.Id, 10m, payPeriod, johnBankAccount.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("does not belong");
    }
}
