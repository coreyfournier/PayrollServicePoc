using PayrollService.Application.Commands.TimeEntry;
using PayrollService.Application.Interfaces;
using PayrollService.Domain.Common;
using PayrollService.Domain.Entities;
using PayrollService.Domain.Repositories;

namespace PayrollService.UnitTests.Application;

public class ClockOutCommandHandlerTests
{
    private readonly ITimeEntryRepository _timeEntryRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ClockOutCommandHandler _handler;

    public ClockOutCommandHandlerTests()
    {
        _timeEntryRepository = Substitute.For<ITimeEntryRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _handler = new ClockOutCommandHandler(_timeEntryRepository, _unitOfWork);

        _unitOfWork.ExecuteAsync(
                Arg.Any<Func<Task>>(),
                Arg.Any<Entity>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.ArgAt<Func<Task>>(0)();
                return Task.CompletedTask;
            });
    }

    [Fact]
    public async Task Handle_WithActiveEntry_ShouldReturnClockedOutDto()
    {
        var employeeId = Guid.NewGuid();
        var activeEntry = TimeEntry.ClockInEmployee(employeeId);

        _timeEntryRepository.GetActiveEntryByEmployeeIdAsync(employeeId, Arg.Any<CancellationToken>())
            .Returns(activeEntry);

        var command = new ClockOutCommand(employeeId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.EmployeeId.Should().Be(employeeId);
        result.ClockOut.Should().NotBeNull();
        result.HoursWorked.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Handle_WithNoActiveEntry_ShouldThrowInvalidOperationException()
    {
        var employeeId = Guid.NewGuid();

        _timeEntryRepository.GetActiveEntryByEmployeeIdAsync(employeeId, Arg.Any<CancellationToken>())
            .Returns((TimeEntry?)null);

        var command = new ClockOutCommand(employeeId);
        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Employee is not clocked in.");
    }

    [Fact]
    public async Task Handle_ShouldCallUnitOfWorkExecuteAsync()
    {
        var employeeId = Guid.NewGuid();
        var activeEntry = TimeEntry.ClockInEmployee(employeeId);

        _timeEntryRepository.GetActiveEntryByEmployeeIdAsync(employeeId, Arg.Any<CancellationToken>())
            .Returns(activeEntry);

        var command = new ClockOutCommand(employeeId);
        await _handler.Handle(command, CancellationToken.None);

        await _unitOfWork.Received(1).ExecuteAsync(
            Arg.Any<Func<Task>>(),
            Arg.Any<Entity>(),
            Arg.Any<CancellationToken>());
    }
}
