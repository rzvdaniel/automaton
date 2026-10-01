using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;
using System.Text.Json;

namespace Automaton.Runner.Console.Tests;

public sealed class AppActionCommandExecutorTests
{
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
