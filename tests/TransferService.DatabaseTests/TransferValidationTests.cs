using Microsoft.Extensions.DependencyInjection;
using TransferService.Application.Interfaces;
using TransferService.Application.Services;
using TransferService.DatabaseTests.Fixtures;
using TransferService.Domain.Entities;
using TransferService.Domain.Enums;
using TransferService.Domain.Repositories;

namespace TransferService.DatabaseTests;

[Collection("TransferMongo")]
public class TransferValidationTests
{
    private readonly MongoDbFixture _fixture;

    public TransferValidationTests(MongoDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Validate_NonExistentBankAccount_Fails()
    {
        using var scope = _fixture.Services.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<ITransferValidationService>();

        var request = new TransferValidationRequest(
            Guid.NewGuid(), 100m, 55, Guid.NewGuid());

        var result = await validator.ValidateAsync(request);
        result.CanTransfer.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.Contains("Bank account"));
    }

    [Fact]
    public async Task Validate_WrongEmployeeBankAccount_Fails()
    {
        using var scope = _fixture.Services.CreateScope();
        var bankRepo = scope.ServiceProvider.GetRequiredService<IBankAccountRepository>();
        var validator = scope.ServiceProvider.GetRequiredService<ITransferValidationService>();

        var ownerEmployeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var account = BankAccount.Create(ownerEmployeeId, "Chase", "****1234", "021000021", BankAccountType.Checking);
        await bankRepo.AddAsync(account);

        var request = new TransferValidationRequest(
            otherEmployeeId, 100m, 55, account.Id);

        var result = await validator.ValidateAsync(request);
        result.CanTransfer.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.Contains("belong"));
    }

    [Fact]
    public async Task Validate_InProgressTransfer_ExcludesCurrentTransferId()
    {
        using var scope = _fixture.Services.CreateScope();
        var transferRepo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
        var bankRepo = scope.ServiceProvider.GetRequiredService<IBankAccountRepository>();
        var validator = scope.ServiceProvider.GetRequiredService<ITransferValidationService>();

        var employeeId = Guid.NewGuid();
        var account = BankAccount.Create(employeeId, "Chase", "****5678", "021000021", BankAccountType.Checking);
        await bankRepo.AddAsync(account);

        // Create an in-progress transfer
        var existing = Transfer.Create(employeeId, 100m, 55, account.Id);
        await transferRepo.AddAsync(existing);

        // Validating with the same transferId should NOT flag duplicate
        var request = new TransferValidationRequest(
            employeeId, 200m, 55, account.Id, existing.Id);

        var result = await validator.ValidateAsync(request);
        result.Reasons.Should().NotContain(r => r.Contains("already in progress"));
    }

    [Fact]
    public async Task Validate_DuplicateInProgressTransfer_Fails()
    {
        using var scope = _fixture.Services.CreateScope();
        var transferRepo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
        var bankRepo = scope.ServiceProvider.GetRequiredService<IBankAccountRepository>();
        var validator = scope.ServiceProvider.GetRequiredService<ITransferValidationService>();

        var employeeId = Guid.NewGuid();
        var account = BankAccount.Create(employeeId, "Chase", "****9012", "021000021", BankAccountType.Checking);
        await bankRepo.AddAsync(account);

        var existing = Transfer.Create(employeeId, 100m, 55, account.Id);
        await transferRepo.AddAsync(existing);

        // Different transferId — should flag duplicate
        var request = new TransferValidationRequest(
            employeeId, 200m, 55, account.Id, Guid.NewGuid());

        var result = await validator.ValidateAsync(request);
        result.CanTransfer.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.Contains("already"));
    }
}
