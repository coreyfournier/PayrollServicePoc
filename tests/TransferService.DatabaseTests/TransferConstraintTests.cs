using Microsoft.Extensions.DependencyInjection;
using TransferService.DatabaseTests.Fixtures;
using TransferService.Domain.Entities;
using TransferService.Domain.Exceptions;
using TransferService.Domain.Repositories;

namespace TransferService.DatabaseTests;

[Collection("TransferMongo")]
public class TransferConstraintTests
{
    private readonly MongoDbFixture _fixture;

    public TransferConstraintTests(MongoDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SecondInProgressTransfer_SameEmployee_ThrowsDuplicateException()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

        var employeeId = Guid.NewGuid();
        var first = Transfer.Create(employeeId, 100m, 55, Guid.NewGuid());
        await repo.AddAsync(first);

        var second = Transfer.Create(employeeId, 200m, 55, Guid.NewGuid());
        var act = () => repo.AddAsync(second);

        await act.Should().ThrowAsync<DuplicateInProgressTransferException>();
    }

    [Fact]
    public async Task CompletedTransfer_AllowsNewTransfer_SameEmployee()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

        var employeeId = Guid.NewGuid();
        var first = Transfer.Create(employeeId, 100m, 55, Guid.NewGuid());
        await repo.AddAsync(first);

        first.MarkCompleted("BNK-ref-123");
        await repo.UpdateAsync(first);

        var second = Transfer.Create(employeeId, 200m, 55, Guid.NewGuid());
        var act = () => repo.AddAsync(second);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FailedTransfer_AllowsNewTransfer_SameEmployee()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

        var employeeId = Guid.NewGuid();
        var first = Transfer.Create(employeeId, 100m, 55, Guid.NewGuid());
        await repo.AddAsync(first);

        first.MarkFailed("Insufficient funds");
        await repo.UpdateAsync(first);

        var second = Transfer.Create(employeeId, 200m, 55, Guid.NewGuid());
        var act = () => repo.AddAsync(second);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DifferentEmployees_CanHaveSimultaneousInProgressTransfers()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();

        var first = Transfer.Create(Guid.NewGuid(), 100m, 55, Guid.NewGuid());
        var second = Transfer.Create(Guid.NewGuid(), 200m, 55, Guid.NewGuid());

        await repo.AddAsync(first);
        var act = () => repo.AddAsync(second);
        await act.Should().NotThrowAsync();
    }
}
