using Microsoft.Extensions.DependencyInjection;
using TransferService.DatabaseTests.Fixtures;
using TransferService.Domain.Entities;
using TransferService.Domain.Repositories;

namespace TransferService.DatabaseTests;

[Collection("TransferMongo")]
public class TransferCrudTests
{
    private readonly MongoDbFixture _fixture;

    public TransferCrudTests(MongoDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AddAndRetrieve_Transfer_RoundTrips()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

        var transfer = Transfer.Create(Guid.NewGuid(), 500m, 55, Guid.NewGuid());
        await repo.AddAsync(transfer);

        var retrieved = await repo.GetByIdAsync(transfer.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Amount.Should().Be(500m);
        retrieved.PayPeriodNumber.Should().Be(55);
        retrieved.Status.Should().Be(TransferService.Domain.Enums.TransferStatus.Initiated);
        retrieved.WorkflowSteps.Should().HaveCount(5);
    }

    [Fact]
    public async Task Update_Transfer_PersistsStateChange()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

        var transfer = Transfer.Create(Guid.NewGuid(), 200m, 55, Guid.NewGuid());
        await repo.AddAsync(transfer);

        transfer.MarkCompleted("BNK-20260320-abc12345");
        await repo.UpdateAsync(transfer);

        var retrieved = await repo.GetByIdAsync(transfer.Id);
        retrieved!.Status.Should().Be(TransferService.Domain.Enums.TransferStatus.Completed);
        retrieved.ExternalReferenceId.Should().Be("BNK-20260320-abc12345");
    }

    [Fact]
    public async Task WorkflowSteps_PersistAndDeserialize()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

        var transfer = Transfer.Create(Guid.NewGuid(), 100m, 55, Guid.NewGuid());
        transfer.CompleteWorkflowStep("Validation", "Passed");
        transfer.StartWorkflowStep("BalanceCheck");
        await repo.AddAsync(transfer);

        var retrieved = await repo.GetByIdAsync(transfer.Id);
        var validationStep = retrieved!.WorkflowSteps.Find(s => s.Name == "Validation");
        validationStep!.Status.Should().Be("Completed");
        var balanceStep = retrieved.WorkflowSteps.Find(s => s.Name == "BalanceCheck");
        balanceStep!.Status.Should().Be("InProgress");
    }
}
