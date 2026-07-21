using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Models;

namespace Automaton.Api.Tests;

public sealed class AppModuleExecutionReconciliationTests
{
    [Test]
    public void DispatchingOlderThanDispatchWindowIsStale()
    {
        var now = DateTime.UtcNow;
        var execution = new AppModuleExecution { Status = AppModuleExecutionStatuses.Dispatching, CreatedAtUtc = now.AddMinutes(-6) };

        Assert.That(AppModuleExecutionReconciliationService.IsStale(execution, now, now.AddMinutes(-5)), Is.True);
    }

    [Test]
    public void RunningExecutionWithinConfiguredDeadlineIsNotStale()
    {
        var now = DateTime.UtcNow;
        var execution = new AppModuleExecution { Status = AppModuleExecutionStatuses.Running, ExecutionDeadlineUtc = now.AddMinutes(25) };

        Assert.That(AppModuleExecutionReconciliationService.IsStale(execution, now, now.AddMinutes(-5)), Is.False);
    }

    [Test]
    public void RunningExecutionPastConfiguredDeadlineIsStale()
    {
        var now = DateTime.UtcNow;
        var execution = new AppModuleExecution { Status = AppModuleExecutionStatuses.Running, ExecutionDeadlineUtc = now.AddSeconds(-1) };

        Assert.That(AppModuleExecutionReconciliationService.IsStale(execution, now, now.AddMinutes(-5)), Is.True);
    }

    [TestCase(AppModuleExecutionStatuses.Succeeded)]
    [TestCase(AppModuleExecutionStatuses.Failed)]
    [TestCase(AppModuleExecutionStatuses.Cancelled)]
    [TestCase(AppModuleExecutionStatuses.Lost)]
    public void TerminalExecutionIsNeverStale(string status)
    {
        var now = DateTime.UtcNow;
        var execution = new AppModuleExecution { Status = status, ExecutionDeadlineUtc = now.AddMinutes(-10) };

        Assert.That(AppModuleExecutionReconciliationService.IsStale(execution, now, now.AddMinutes(-5)), Is.False);
    }
}
