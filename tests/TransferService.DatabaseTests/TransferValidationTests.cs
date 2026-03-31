using Microsoft.Extensions.DependencyInjection;
using TransferService.Application.Interfaces;
using TransferService.Application.Services;
using TransferService.DatabaseTests.Fixtures;
using TransferService.Domain.Entities;
using TransferService.Domain.Repositories;

namespace TransferService.DatabaseTests;

[Collection("TransferMongo")]
public class TransferValidationTests
{
    private readonly MongoDbFixture _fixture;

    public TransferValidationTests(MongoDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Validate_InProgressTransfer_ExcludesCurrentTransferId()
    {
        using var scope = _fixture.Services.CreateScope();
        var transferRepo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
        var validator = scope.ServiceProvider.GetRequiredService<ITransferValidationService>();

        var employeeId = Guid.NewGuid();
        var bankAccountId = Guid.NewGuid();

        // Create an in-progress transfer
        var existing = Transfer.Create(employeeId, 100m, 55, bankAccountId);
        await transferRepo.AddAsync(existing);

        // Validating with the same transferId should NOT flag duplicate
        var request = new TransferValidationRequest(
            employeeId, 200m, 55, bankAccountId, existing.Id);

        var result = await validator.ValidateAsync(request);
        result.Reasons.Should().NotContain(r => r.Contains("already in progress"));
    }

    [Fact]
    public async Task Validate_DuplicateInProgressTransfer_Fails()
    {
        using var scope = _fixture.Services.CreateScope();
        var transferRepo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
        var validator = scope.ServiceProvider.GetRequiredService<ITransferValidationService>();

        var employeeId = Guid.NewGuid();
        var bankAccountId = Guid.NewGuid();

        var existing = Transfer.Create(employeeId, 100m, 55, bankAccountId);
        await transferRepo.AddAsync(existing);

        // Different transferId — should flag duplicate
        var request = new TransferValidationRequest(
            employeeId, 200m, 55, bankAccountId, Guid.NewGuid());

        var result = await validator.ValidateAsync(request);
        result.CanTransfer.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.Contains("already"));
    }
}
