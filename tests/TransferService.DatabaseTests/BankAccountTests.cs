using Microsoft.Extensions.DependencyInjection;
using TransferService.DatabaseTests.Fixtures;
using TransferService.Domain.Entities;
using TransferService.Domain.Enums;
using TransferService.Domain.Repositories;

namespace TransferService.DatabaseTests;

[Collection("TransferMongo")]
public class BankAccountTests
{
    private readonly MongoDbFixture _fixture;

    public BankAccountTests(MongoDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AddAndRetrieve_BankAccount_RoundTrips()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBankAccountRepository>();

        var employeeId = Guid.NewGuid();
        var account = BankAccount.Create(employeeId, "Chase Bank", "****1234", "021000021", BankAccountType.Checking);
        await repo.AddAsync(account);

        var retrieved = await repo.GetByIdAsync(account.Id);
        retrieved.Should().NotBeNull();
        retrieved!.EmployeeId.Should().Be(employeeId);
        retrieved.BankName.Should().Be("Chase Bank");
        retrieved.AccountType.Should().Be(BankAccountType.Checking);
    }

    [Fact]
    public async Task GetByEmployeeId_ReturnsOnlyMatchingAccounts()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBankAccountRepository>();

        var employeeId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await repo.AddAsync(BankAccount.Create(employeeId, "Chase", "****1111", "021000021", BankAccountType.Checking));
        await repo.AddAsync(BankAccount.Create(otherId, "Chase", "****2222", "021000021", BankAccountType.Savings));

        var accounts = await repo.GetByEmployeeIdAsync(employeeId);
        accounts.Should().ContainSingle();
        accounts.First().EmployeeId.Should().Be(employeeId);
    }
}
