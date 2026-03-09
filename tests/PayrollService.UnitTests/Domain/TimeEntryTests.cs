using PayrollService.Domain.Entities;
using PayrollService.Domain.Events;

namespace PayrollService.UnitTests.Domain;

public class TimeEntryTests
{
    [Fact]
    public void ClockInEmployee_ShouldCreateEntryWithNoClockOut()
    {
        var employeeId = Guid.NewGuid();

        var entry = TimeEntry.ClockInEmployee(employeeId);

        entry.EmployeeId.Should().Be(employeeId);
        entry.ClockOut.Should().BeNull();
        entry.HoursWorked.Should().Be(0);
        entry.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<EmployeeClockedInEvent>();
    }

    [Fact]
    public void Create_WithValidClockOut_ShouldCalculateHoursWorked()
    {
        var employeeId = Guid.NewGuid();
        var clockIn = new DateTime(2024, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        var clockOut = new DateTime(2024, 1, 15, 16, 30, 0, DateTimeKind.Utc);

        var entry = TimeEntry.Create(employeeId, clockIn, clockOut);

        entry.EmployeeId.Should().Be(employeeId);
        entry.ClockIn.Should().Be(clockIn);
        entry.ClockOut.Should().Be(clockOut);
        entry.HoursWorked.Should().Be(8.5m);
        entry.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<EmployeeClockedOutEvent>();
    }

    [Fact]
    public void Create_WithNullClockOut_ShouldHaveZeroHours()
    {
        var employeeId = Guid.NewGuid();
        var clockIn = new DateTime(2024, 1, 15, 8, 0, 0, DateTimeKind.Utc);

        var entry = TimeEntry.Create(employeeId, clockIn, null);

        entry.ClockOut.Should().BeNull();
        entry.HoursWorked.Should().Be(0);
        entry.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<EmployeeClockedInEvent>();
    }

    [Fact]
    public void Create_WithClockOutBeforeClockIn_ShouldThrow()
    {
        var employeeId = Guid.NewGuid();
        var clockIn = new DateTime(2024, 1, 15, 16, 0, 0, DateTimeKind.Utc);
        var clockOut = new DateTime(2024, 1, 15, 8, 0, 0, DateTimeKind.Utc);

        var act = () => TimeEntry.Create(employeeId, clockIn, clockOut);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Clock out must be after clock in.");
    }

    [Fact]
    public void Create_WithClockOutEqualToClockIn_ShouldThrow()
    {
        var employeeId = Guid.NewGuid();
        var clockIn = new DateTime(2024, 1, 15, 8, 0, 0, DateTimeKind.Utc);

        var act = () => TimeEntry.Create(employeeId, clockIn, clockIn);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ClockOutEmployee_ShouldSetClockOutAndCalculateHours()
    {
        var entry = TimeEntry.ClockInEmployee(Guid.NewGuid());
        entry.ClearDomainEvents();

        entry.ClockOutEmployee();

        entry.ClockOut.Should().NotBeNull();
        entry.HoursWorked.Should().BeGreaterThanOrEqualTo(0);
        entry.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<EmployeeClockedOutEvent>();
    }

    [Fact]
    public void ClockOutEmployee_WhenAlreadyClockedOut_ShouldThrow()
    {
        var entry = TimeEntry.ClockInEmployee(Guid.NewGuid());
        entry.ClockOutEmployee();

        var act = () => entry.ClockOutEmployee();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Employee has already clocked out for this entry.");
    }

    [Fact]
    public void UpdateTimes_WithValidTimes_ShouldUpdateAndRaiseEvent()
    {
        var entry = TimeEntry.ClockInEmployee(Guid.NewGuid());
        entry.ClearDomainEvents();
        var newClockIn = new DateTime(2024, 1, 15, 9, 0, 0, DateTimeKind.Utc);
        var newClockOut = new DateTime(2024, 1, 15, 17, 0, 0, DateTimeKind.Utc);

        entry.UpdateTimes(newClockIn, newClockOut);

        entry.ClockIn.Should().Be(newClockIn);
        entry.ClockOut.Should().Be(newClockOut);
        entry.HoursWorked.Should().Be(8.0m);
        entry.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TimeEntryUpdatedEvent>();
    }

    [Fact]
    public void UpdateTimes_WithInvalidClockOut_ShouldThrow()
    {
        var entry = TimeEntry.ClockInEmployee(Guid.NewGuid());
        var clockIn = new DateTime(2024, 1, 15, 17, 0, 0, DateTimeKind.Utc);
        var clockOut = new DateTime(2024, 1, 15, 9, 0, 0, DateTimeKind.Utc);

        var act = () => entry.UpdateTimes(clockIn, clockOut);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Clock out must be after clock in.");
    }

    [Fact]
    public void UpdateTimes_WithNullClockOut_ShouldSetZeroHours()
    {
        var entry = TimeEntry.Create(Guid.NewGuid(),
            new DateTime(2024, 1, 15, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 15, 16, 0, 0, DateTimeKind.Utc));
        entry.ClearDomainEvents();

        entry.UpdateTimes(new DateTime(2024, 1, 15, 9, 0, 0, DateTimeKind.Utc), null);

        entry.HoursWorked.Should().Be(0);
    }
}
