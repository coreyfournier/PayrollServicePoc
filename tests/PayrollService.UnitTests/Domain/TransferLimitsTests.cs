using PayrollService.Domain.ValueObjects;

namespace PayrollService.UnitTests.Domain;

public class TransferLimitsTests
{
    [Fact]
    public void Default_ShouldHaveExpectedValues()
    {
        var limits = TransferLimits.Default;

        limits.MaxTransfersPerPayPeriod.Should().Be(5);
        limits.MaxAmountPerPayPeriod.Should().Be(10000m);
        limits.MaxTransfersPerDay.Should().Be(1);
    }

    [Fact]
    public void Validate_WithinAllLimits_ShouldReturnCanTransfer()
    {
        var limits = TransferLimits.Default;

        var result = limits.Validate(
            currentPeriodCount: 0,
            currentPeriodAmount: 0m,
            requestedAmount: 500m,
            transfersToday: 0);

        result.CanTransfer.Should().BeTrue();
        result.Reasons.Should().BeEmpty();
    }

    [Fact]
    public void Validate_DailyLimitReached_ShouldReturnCannotTransfer()
    {
        var limits = TransferLimits.Default;

        var result = limits.Validate(
            currentPeriodCount: 0,
            currentPeriodAmount: 0m,
            requestedAmount: 500m,
            transfersToday: 1);

        result.CanTransfer.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.Contains("Daily transfer limit"));
    }

    [Fact]
    public void Validate_PeriodCountLimitReached_ShouldReturnCannotTransfer()
    {
        var limits = TransferLimits.Default;

        var result = limits.Validate(
            currentPeriodCount: 5,
            currentPeriodAmount: 1000m,
            requestedAmount: 500m,
            transfersToday: 0);

        result.CanTransfer.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.Contains("Pay period transfer limit"));
    }

    [Fact]
    public void Validate_PeriodAmountLimitExceeded_ShouldReturnCannotTransfer()
    {
        var limits = TransferLimits.Default;

        var result = limits.Validate(
            currentPeriodCount: 0,
            currentPeriodAmount: 9800m,
            requestedAmount: 300m,
            transfersToday: 0);

        result.CanTransfer.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.Contains("amount limit would be exceeded"));
    }

    [Fact]
    public void Validate_MultipleViolations_ShouldReturnAllReasons()
    {
        var limits = TransferLimits.Default;

        var result = limits.Validate(
            currentPeriodCount: 5,
            currentPeriodAmount: 9800m,
            requestedAmount: 300m,
            transfersToday: 1);

        result.CanTransfer.Should().BeFalse();
        result.Reasons.Should().HaveCount(3);
    }

    [Fact]
    public void Validate_ExactlyAtPeriodAmountLimit_ShouldReturnCanTransfer()
    {
        var limits = TransferLimits.Default;

        var result = limits.Validate(
            currentPeriodCount: 0,
            currentPeriodAmount: 9500m,
            requestedAmount: 500m,
            transfersToday: 0);

        result.CanTransfer.Should().BeTrue();
    }
}
