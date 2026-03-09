using PayrollService.Application.Commands.Employee;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Common;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Repositories;

namespace PayrollService.UnitTests.Application;

public class CreateEmployeeCommandHandlerTests
{
    private readonly IEmployeeRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CreateEmployeeCommandHandler _handler;

    public CreateEmployeeCommandHandlerTests()
    {
        _repository = Substitute.For<IEmployeeRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _handler = new CreateEmployeeCommandHandler(_repository, _unitOfWork);

        _unitOfWork.ExecuteAsync(
                Arg.Any<Func<Task<Employee>>>(),
                Arg.Any<Entity>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.ArgAt<Func<Task<Employee>>>(0)());
    }

    [Fact]
    public async Task Handle_ShouldReturnEmployeeDto()
    {
        var hireDate = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var command = new CreateEmployeeCommand("John", "Doe", "john@test.com", PayType.Hourly, 25.50m, hireDate);

        _repository.AddAsync(Arg.Any<Employee>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.ArgAt<Employee>(0)));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.FirstName.Should().Be("John");
        result.LastName.Should().Be("Doe");
        result.Email.Should().Be("john@test.com");
        result.PayType.Should().Be(PayType.Hourly);
        result.PayRate.Should().Be(25.50m);
        result.HireDate.Should().Be(hireDate);
        result.IsActive.Should().BeTrue();
        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldCallUnitOfWorkExecuteAsync()
    {
        var command = new CreateEmployeeCommand("John", "Doe", "john@test.com", PayType.Hourly, 25.50m, DateTime.UtcNow);

        _repository.AddAsync(Arg.Any<Employee>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.ArgAt<Employee>(0)));

        await _handler.Handle(command, CancellationToken.None);

        await _unitOfWork.Received(1).ExecuteAsync(
            Arg.Any<Func<Task<Employee>>>(),
            Arg.Any<Entity>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithCustomPayPeriodHours_ShouldPassToDto()
    {
        var command = new CreateEmployeeCommand("Jane", "Doe", "jane@test.com", PayType.Salary, 75000m, DateTime.UtcNow, 35);

        _repository.AddAsync(Arg.Any<Employee>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.ArgAt<Employee>(0)));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.PayPeriodHours.Should().Be(35m);
    }
}
