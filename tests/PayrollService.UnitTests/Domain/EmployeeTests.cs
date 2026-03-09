using PayrollService.Domain.Entities;
using PayrollService.Domain.Enums;
using PayrollService.Domain.Events;

namespace PayrollService.UnitTests.Domain;

public class EmployeeTests
{
    [Fact]
    public void Create_ShouldSetPropertiesCorrectly()
    {
        var hireDate = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        var employee = Employee.Create("John", "Doe", "john@test.com", PayType.Hourly, 25.50m, hireDate);

        employee.FirstName.Should().Be("John");
        employee.LastName.Should().Be("Doe");
        employee.Email.Should().Be("john@test.com");
        employee.PayType.Should().Be(PayType.Hourly);
        employee.PayRate.Should().Be(25.50m);
        employee.PayPeriodHours.Should().Be(40m);
        employee.HireDate.Should().Be(hireDate);
        employee.IsActive.Should().BeTrue();
        employee.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_ShouldRaiseEmployeeCreatedEvent()
    {
        var employee = Employee.Create("John", "Doe", "john@test.com", PayType.Hourly, 25.50m, DateTime.UtcNow);

        employee.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<EmployeeCreatedEvent>();
    }

    [Fact]
    public void Create_WithCustomPayPeriodHours_ShouldSetPayPeriodHours()
    {
        var employee = Employee.Create("Jane", "Doe", "jane@test.com", PayType.Salary, 75000m, DateTime.UtcNow, 35);

        employee.PayPeriodHours.Should().Be(35m);
    }

    [Fact]
    public void Update_ShouldModifyPropertiesAndRaiseEvent()
    {
        var employee = Employee.Create("John", "Doe", "john@test.com", PayType.Hourly, 25.50m, DateTime.UtcNow);
        employee.ClearDomainEvents();

        employee.Update("Jane", "Smith", "jane@test.com", PayType.Salary, 80000m, 35);

        employee.FirstName.Should().Be("Jane");
        employee.LastName.Should().Be("Smith");
        employee.Email.Should().Be("jane@test.com");
        employee.PayType.Should().Be(PayType.Salary);
        employee.PayRate.Should().Be(80000m);
        employee.PayPeriodHours.Should().Be(35m);
        employee.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<EmployeeUpdatedEvent>();
    }

    [Fact]
    public void Deactivate_ShouldSetIsActiveFalseAndRaiseEvent()
    {
        var employee = Employee.Create("John", "Doe", "john@test.com", PayType.Hourly, 25.50m, DateTime.UtcNow);
        employee.ClearDomainEvents();

        employee.Deactivate();

        employee.IsActive.Should().BeFalse();
        employee.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<EmployeeDeactivatedEvent>();
    }

    [Fact]
    public void Activate_ShouldSetIsActiveTrueAndRaiseEvent()
    {
        var employee = Employee.Create("John", "Doe", "john@test.com", PayType.Hourly, 25.50m, DateTime.UtcNow);
        employee.Deactivate();
        employee.ClearDomainEvents();

        employee.Activate();

        employee.IsActive.Should().BeTrue();
        employee.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<EmployeeActivatedEvent>();
    }

    [Fact]
    public void ClearDomainEvents_ShouldRemoveAllEvents()
    {
        var employee = Employee.Create("John", "Doe", "john@test.com", PayType.Hourly, 25.50m, DateTime.UtcNow);

        employee.ClearDomainEvents();

        employee.DomainEvents.Should().BeEmpty();
    }
}
