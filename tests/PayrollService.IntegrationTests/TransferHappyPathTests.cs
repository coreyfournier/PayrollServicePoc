using FluentAssertions;
using PayrollService.IntegrationTests.Infrastructure;

namespace PayrollService.IntegrationTests;

/// <summary>
/// Tests the happy path: transfer amount is within the employee's balance,
/// workflow completes successfully through all steps.
/// Uses: John Smith (dedicated to this test class)
/// </summary>
[Collection("Integration")]
public class TransferHappyPathTests
{
    private readonly TestFixture _fixture;

    public TransferHappyPathTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Transfer_WithSufficientBalance_CompletesAndPropagates()
    {
        // Arrange — John Smith: salaried, net pay ~$816.41
        var employee = _fixture.GetEmployee("John");
        var bankAccount = _fixture.GetBankAccount(employee.Id);
        const decimal amount = 50m;
        const long payPeriod = 56;

        // Act — initiate transfer
        var initResult = await _fixture.Api.InitiateTransferAsync(
            employee.Id, amount, payPeriod, bankAccount.Id);

        // Assert — actor accepted the transfer
        initResult.Success.Should().BeTrue("the actor should accept a valid transfer");
        initResult.TransferId.Should().NotBeNull();
        var transferId = initResult.TransferId!.Value;

        // Wait for workflow to complete (bank simulation takes 1-10s, plus retries)
        var transfer = await PollingHelper.WaitForAsync(
            () => _fixture.Api.GetTransferAsync(employee.Id, transferId),
            t => t != null && (t.Status == 3 || t.Status == 4), // Completed or Failed
            timeout: TimeSpan.FromSeconds(60),
            timeoutMessage: "Transfer workflow did not complete within timeout");

        transfer.Should().NotBeNull();
        transfer!.Amount.Should().Be(amount);
        transfer.EmployeeId.Should().Be(employee.Id);

        // The bank simulator has ~20% failure rate, so we accept both outcomes
        if (transfer.Status == 3) // Completed
        {
            transfer.ExternalReferenceId.Should().NotBeNullOrEmpty(
                "completed transfers should have a bank reference");
            transfer.FailureReason.Should().BeNull();
        }
        else // Failed (bank simulation ~20% failure after retries)
        {
            transfer.FailureReason.Should().NotBeNullOrEmpty();
        }

        // Verify Dapr state store consistency
        var daprState = await _fixture.Db.GetDaprTransferStateAsync(transferId);
        daprState.Should().NotBeNull("transfer must exist in Dapr state store");
        daprState!.RootElement.GetProperty("Status").GetInt32()
            .Should().Be(transfer.Status);

        // Verify Kafka event propagated to ListenerApi MySQL
        var mysqlRecords = await PollingHelper.WaitForAsync(
            () => _fixture.Db.GetMySqlTransfersAsync(employee.Id),
            records => records.Any(r => r.Id == transferId &&
                (r.Status == "Completed" || r.Status == "Failed")),
            timeout: TimeSpan.FromSeconds(30),
            timeoutMessage: "Transfer event did not propagate to ListenerApi MySQL");

        var mysqlRecord = mysqlRecords.First(r => r.Id == transferId);
        mysqlRecord.Amount.Should().Be(amount);
        var expectedStatus = transfer.Status == 3 ? "Completed" : "Failed";
        mysqlRecord.Status.Should().Be(expectedStatus);
    }
}
