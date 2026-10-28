using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Runner.Console.Tests;

public sealed class AppActionCommandExecutorTests
{
    [Test]
    public async Task ExecutionDispatcherUsesTheRegisteredCommandHandler()
    {
        var handler = new AppActionCommandExecutor(new AppActionExecutionService(new RunnerWorkspaceService()), null!, null!);
        using var services = new ServiceCollection().AddSingleton(handler).BuildServiceProvider();
        using var dispatcher = new RunnerExecutionExecutor(services);
        var result = await dispatcher.ExecuteAsync(new(null, null,
            AppActionCommand.Create("read-logs", new ReadAppModuleExecutionArtifactRequest(
                Guid.NewGuid(), Guid.NewGuid(), "missing", Guid.NewGuid()))), CancellationToken.None);
        Assert.That(result.Outcome, Is.EqualTo("Succeeded"));
    }

    [Test]
    public void UnknownCommandIsPermanentlyInvalid()
        => Assert.ThrowsAsync<InvalidDataException>(() => new AppActionCommandExecutor(null!, null!, null!)
            .ExecuteAsync(AppActionCommand.Create("unknown", new { }), CancellationToken.None));
    [TestCase("read-logs")]
    [TestCase("read-result")]
    public async Task MissingArtifactsReturnUnavailableThroughHttpsResultPayload(string kind)
    {
        var service = new AppActionExecutionService(new RunnerWorkspaceService());
        var executor = new AppActionCommandExecutor(service, null!, null!);
        var response = await executor.ExecuteAsync(AppActionCommand.Create(kind,
            new ReadAppModuleExecutionArtifactRequest(Guid.NewGuid(), Guid.NewGuid(), "missing", Guid.NewGuid())), CancellationToken.None);
        Assert.That(response.Outcome, Is.EqualTo("Succeeded"));
        Assert.That(response.ActionResult!.Value.Deserialize<AppModuleExecutionArtifactResponse>()!.Available, Is.False);
    }
    [TestCase("list-modules")]
    [TestCase("list-versions")]
    public async Task ModuleManagementUsesExistingDeployedWorkspace(string kind)
    {
        var executor = new AppActionCommandExecutor(null!, new(new RunnerWorkspaceService()), null!);
        var response = await executor.ExecuteAsync(AppActionCommand.Create(kind,
            new AppActionModuleRequest(Guid.NewGuid(), Guid.NewGuid(), "missing")), CancellationToken.None);
        Assert.That(response.Outcome, Is.EqualTo("Succeeded")); Assert.That(response.ActionResult, Is.Not.Null);
    }
}
