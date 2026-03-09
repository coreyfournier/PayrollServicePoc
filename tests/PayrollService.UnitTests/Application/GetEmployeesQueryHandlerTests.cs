using PayrollService.Application.Queries.Employee;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Repositories;

namespace PayrollService.UnitTests.Application;

public class GetEmployeesQueryHandlerTests
{
    private readonly IEmployeeRepository _repository;
    private readonly GetEmployeesQueryHandler _handler;

    public GetEmployeesQueryHandlerTests()
    {
        _repository = Substitute.For<IEmployeeRepository>();
        _handler = new GetEmployeesQueryHandler(_repository);
    }

    [Fact]
    public async Task Handle_WithEmployees_ShouldReturnDtos()
    {
        var employees = new[]
        {
            Employee.Create("John", "Doe", "john@test.com", PayType.Hourly, 25.50m, DateTime.UtcNow),
            Employee.Create("Jane", "Smith", "jane@test.com", PayType.Salary, 75000m, DateTime.UtcNow)
        };

        _repository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(employees.AsEnumerable());

        var result = await _handler.Handle(new GetEmployeesQuery(), CancellationToken.None);

        var dtos = result.ToList();
        dtos.Should().HaveCount(2);
        dtos[0].FirstName.Should().Be("John");
        dtos[0].PayType.Should().Be(PayType.Hourly);
        dtos[1].FirstName.Should().Be("Jane");
        dtos[1].PayType.Should().Be(PayType.Salary);
    }

    [Fact]
    public async Task Handle_WithNoEmployees_ShouldReturnEmptyCollection()
    {
        _repository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Enumerable.Empty<Employee>());

        var result = await _handler.Handle(new GetEmployeesQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
