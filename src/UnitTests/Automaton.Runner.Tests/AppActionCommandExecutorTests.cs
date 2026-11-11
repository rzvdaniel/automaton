using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Runner.Console.Tests;

public sealed class AppActionCommandExecutorTests
{
    [TestCase("restart-runtime")]
    [TestCase("resume-runtime")]
    public void RestartAndResumeThatDidNothingFailInsteadOfSucceeding(string kind)
    {
        var workspace = new RunnerWorkspaceService();
        var runtime = new RunnerDeploymentService(workspace, new NoopProcessManager(), new AppActionDeploymentService(workspace));
        var executor = new AppActionCommandExecutor(null!, null!, runtime);
        object request = kind == "restart-runtime"
            ? new RestartAppRuntimeRequest(Guid.NewGuid(), Guid.NewGuid())
            : new ResumeAppRuntimeRequest(Guid.NewGuid(), Guid.NewGuid());
        Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.ExecuteAsync(AppActionCommand.Create(kind, request), CancellationToken.None));
    }

    [Test]
    public async Task ExecutionDispatcherUsesTheRegisteredCommandHandler()
    {
        using var services = RegisteredHandlerServices();
        using var dispatcher = new RunnerExecutionExecutor(services);
        var result = await dispatcher.ExecuteAsync(new(
            AppActionCommand.Create("read-logs", new ReadAppModuleExecutionArtifactRequest(
                Guid.NewGuid(), Guid.NewGuid(), "missing", Guid.NewGuid()))), CancellationToken.None);
        Assert.That(result.Outcome, Is.EqualTo("Succeeded"));
    }

    private static ServiceProvider RegisteredHandlerServices()
    {
        var handler = new AppActionCommandExecutor(new AppActionExecutionService(new RunnerWorkspaceService()), null!, null!);
        return new ServiceCollection().AddSingleton(handler).BuildServiceProvider();
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

    private sealed class NoopProcessManager : IAppRuntimeProcessManager
    {
        public Task<RuntimeApplyResult> ApplyAsync(Guid appId, Guid deploymentTargetId, ApplicationContract contract,
            CancellationToken cancellationToken = default, string? artifactRoot = null, Guid? candidateId = null,
            Guid? deploymentAttemptId = null) => Task.FromResult(new RuntimeApplyResult());
        public Task StopAsync(Guid appId, Guid deploymentTargetId) => Task.CompletedTask;
    }
}
