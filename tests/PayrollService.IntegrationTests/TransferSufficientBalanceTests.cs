using FluentAssertions;
using PayrollService.IntegrationTests.Infrastructure;

namespace PayrollService.IntegrationTests;

/// <summary>
/// Tests that transfers within the employee's balance complete without
/// prompting for confirmation. Verifies the ksqlDB balance check correctly
/// identifies sufficient funds and lets the workflow proceed.
/// Uses: Michael Williams (dedicated to this test class)
/// </summary>
[Collection("Integration")]
public class TransferSufficientBalanceTests
{
    private readonly TestFixture _fixture;

    private const int StatusAwaitingConfirmation = 5;
    private const int StatusCompleted = 3;
    private const int StatusFailed = 4;

    public TransferSufficientBalanceTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Transfer_WithSufficientBalance_ShouldNotPromptForConfirmation()
    {
        // Arrange — Michael Williams: net pay ~$1017.84, request well within balance
        var employee = _fixture.GetEmployee("Michael");
        var bankAccount = _fixture.GetBankAccount(employee.Id);
        const decimal amount = 50m;
        var payPeriod = TestFixture.CurrentPayPeriod;

        // Ensure no in-progress transfers block this test (other tests may leave AwaitingConfirmation)
        var existing = await _fixture.Api.GetTransfersByEmployeeAsync(employee.Id);
        foreach (var t in existing.Where(t => t.Status == StatusAwaitingConfirmation))
            await _fixture.Api.AcceptBalanceChangeAsync(t.Id, accepted: false);

        // Wait for rejections to process
        if (existing.Any(t => t.Status == StatusAwaitingConfirmation))
            await Task.Delay(TimeSpan.FromSeconds(10));

        // Act — initiate transfer
        var initResult = await _fixture.Api.InitiateTransferAsync(
            employee.Id, amount, payPeriod, bankAccount.Id);
        initResult.Success.Should().BeTrue();
        var transferId = initResult.TransferId!.Value;

        // Wait for terminal state — should reach Completed or Failed (bank sim)
        // without ever hitting AwaitingConfirmation
        var transfer = await PollingHelper.WaitForAsync(
            () => _fixture.Api.GetTransferAsync(employee.Id, transferId),
            t => t != null && (t.Status == StatusCompleted || t.Status == StatusFailed),
            timeout: TimeSpan.FromSeconds(60),
            timeoutMessage: "Transfer did not reach terminal state — may be stuck at AwaitingConfirmation");

        // Assert — workflow completed without balance confirmation prompt
        transfer.Should().NotBeNull();
        transfer!.Status.Should().BeOneOf(new[] { StatusCompleted, StatusFailed },
            "transfer with sufficient balance should proceed to bank transfer without prompting");
        transfer.Status.Should().NotBe(StatusAwaitingConfirmation,
            "balance was sufficient — should not have prompted for confirmation");
    }
}
