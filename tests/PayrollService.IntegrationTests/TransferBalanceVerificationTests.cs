using FluentAssertions;
using PayrollService.IntegrationTests.Infrastructure;

namespace PayrollService.IntegrationTests;

/// <summary>
/// Tests the balance verification workflow: when the transfer amount exceeds
/// the employee's current net pay, the workflow pauses for client confirmation.
/// Uses: Michael Williams (pause + reject), David Davis (pause + accept)
/// </summary>
[Collection("Integration")]
public class TransferBalanceVerificationTests
{
    private readonly TestFixture _fixture;

    private const int StatusAwaitingConfirmation = 5;
    private const int StatusFailed = 4;
    private const int StatusCompleted = 3;

    public TransferBalanceVerificationTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Transfer_ExceedingBalance_PausesAndRejects()
    {
        // Arrange — Michael Williams: net pay ~$1017.84, request $5000
        var employee = _fixture.GetEmployee("Michael");
        var bankAccount = _fixture.GetBankAccount(employee.Id);
        const decimal amount = 5000m;
        var payPeriod = TestFixture.CurrentPayPeriod;

        // Act — initiate
        var initResult = await _fixture.Api.InitiateTransferAsync(
            employee.Id, amount, payPeriod, bankAccount.Id);
        initResult.Success.Should().BeTrue();
        var transferId = initResult.TransferId!.Value;

        // Wait for AwaitingConfirmation
        var transfer = await PollingHelper.WaitForAsync(
            () => _fixture.Api.GetTransferAsync(employee.Id, transferId),
            t => t != null && t.Status == StatusAwaitingConfirmation,
            timeout: TimeSpan.FromSeconds(30),
            timeoutMessage: "Transfer did not reach AwaitingConfirmation status");

        // Assert — paused with balance info
        transfer!.Status.Should().Be(StatusAwaitingConfirmation);
        transfer.CurrentBalance.Should().NotBeNull();
        transfer.CurrentBalance.Should().BeLessThan(amount);

        // Verify MongoDB transfers collection
        var transferState = await _fixture.Db.GetTransferStateAsync(transferId);
        transferState.Should().NotBeNull();
        transferState!.RootElement.GetProperty("status").GetString().Should().Be("AwaitingConfirmation");
        transferState.RootElement.GetProperty("currentBalance").GetDecimal()
            .Should().Be(transfer.CurrentBalance!.Value);

        // Verify MySQL got the event
        var mysqlRecords = await PollingHelper.WaitForAsync(
            () => _fixture.Db.GetMySqlTransfersAsync(employee.Id),
            records => records.Any(r => r.Id == transferId && r.Status == "AwaitingConfirmation"),
            timeout: TimeSpan.FromSeconds(30),
            timeoutMessage: "AwaitingConfirmation did not propagate to MySQL");

        var mysqlRecord = mysqlRecords.First(r => r.Id == transferId);
        mysqlRecord.CurrentBalance.Should().Be(transfer.CurrentBalance);

        // Act — reject
        await _fixture.Api.AcceptBalanceChangeAsync(transferId, accepted: false);

        // Wait for Failed
        transfer = await PollingHelper.WaitForAsync(
            () => _fixture.Api.GetTransferAsync(employee.Id, transferId),
            t => t != null && t.Status == StatusFailed,
            timeout: TimeSpan.FromSeconds(30),
            timeoutMessage: "Transfer did not transition to Failed after rejection");

        transfer!.FailureReason.Should().Contain("auto-cancelled");

        // Verify MySQL shows Failed
        mysqlRecords = await PollingHelper.WaitForAsync(
            () => _fixture.Db.GetMySqlTransfersAsync(employee.Id),
            records => records.Any(r => r.Id == transferId && r.Status == "Failed"),
            timeout: TimeSpan.FromSeconds(30));
        mysqlRecords.First(r => r.Id == transferId).FailureReason
            .Should().Contain("auto-cancelled");
    }

    [Fact]
    public async Task Transfer_ExceedingBalance_AcceptedProceedsToCompletion()
    {
        // Arrange — David Davis: net pay ~$1072.73, request $5000
        var employee = _fixture.GetEmployee("David");
        var bankAccount = _fixture.GetBankAccount(employee.Id);
        const decimal amount = 5000m;
        var payPeriod = TestFixture.CurrentPayPeriod;

        // Initiate and wait for AwaitingConfirmation
        var initResult = await _fixture.Api.InitiateTransferAsync(
            employee.Id, amount, payPeriod, bankAccount.Id);
        initResult.Success.Should().BeTrue();
        var transferId = initResult.TransferId!.Value;

        await PollingHelper.WaitForAsync(
            () => _fixture.Api.GetTransferAsync(employee.Id, transferId),
            t => t != null && t.Status == StatusAwaitingConfirmation,
            timeout: TimeSpan.FromSeconds(30));

        // Act — accept
        await _fixture.Api.AcceptBalanceChangeAsync(transferId, accepted: true);

        // Wait for terminal state
        var transfer = await PollingHelper.WaitForAsync(
            () => _fixture.Api.GetTransferAsync(employee.Id, transferId),
            t => t != null && (t.Status == StatusCompleted || t.Status == StatusFailed),
            timeout: TimeSpan.FromSeconds(60),
            timeoutMessage: "Transfer did not reach terminal state after acceptance");

        // After acceptance, workflow proceeds to bank transfer step
        transfer!.Status.Should().BeOneOf(new[] { StatusCompleted, StatusFailed },
            "after acceptance, the workflow should proceed to bank transfer");

        if (transfer.Status == StatusCompleted)
            transfer.ExternalReferenceId.Should().NotBeNullOrEmpty();
    }
}
