using Automaton.Core.Models;
using Automaton.Core.Enums;
using Automaton.Runner.Console.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
[NonParallelizable]
public sealed class BrokeredApplicationContainerRuntimeTests
{
    private string? previousHostedRunnerId;
    private string? previousMachineRunnerId;
    private readonly Guid hostedRunnerId = Guid.NewGuid();
    private readonly Guid runnerHostId = Guid.NewGuid();
    private readonly List<HostContainerExecutionRequest> requests = [];

    [SetUp]
    public void SetUp()
    {
        requests.Clear();
        previousHostedRunnerId = Environment.GetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID");
        previousMachineRunnerId = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_ID");
        Environment.SetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID", hostedRunnerId.ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_ID", Guid.NewGuid().ToString());
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID", previousHostedRunnerId);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_ID", previousMachineRunnerId);
    }

    [Test]
    public async Task CreateSendsTypedCandidateAndOriginalStorageWithoutResourcesOrPhysicalPaths()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var storage = new ApplicationStorageSpec
        {
            DataDirectory = "state",
            Mounts = [new() { Name = "uploads", RelativePath = "files", ReadOnly = true }]
        };
        var runtime = Runtime(_ => new()
        {
            Success = true,
            ContainerId = "container-id",
            ContainerName = "container-name",
            ResolvedImage = "example/runtime@sha256:" + new string('a', 64)
        });

        await runtime.CreateApplicationAsync(new(
            "ignored-direct-name", "example/runtime:latest", @"C:\runner\artifacts\candidate-x\application", ".",
            new() { Executable = "serve" }, 8080, 0, @"C:\runner\data", new Dictionary<string, string>(),
            appId, targetId, candidateId, storage, "1000:1000"));

        var request = requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.Operation, Is.EqualTo(HostContainerOperationKinds.CreateApplicationContainer));
            Assert.That(request.OperationId, Is.EqualTo(Guid.Empty), "the API client assigns a fresh operation per call");
            Assert.That(request.Scope.AppId, Is.EqualTo(appId));
            Assert.That(request.Scope.DeploymentTargetId, Is.EqualTo(targetId));
            Assert.That(request.CandidateId, Is.EqualTo(candidateId));
            Assert.That(request.Resources, Is.Null);
            Assert.That(request.Storage, Is.SameAs(storage));
            Assert.That(request.Storage!.DataDirectory, Is.EqualTo("state"));
            Assert.That(request.Storage.Mounts.Single().RelativePath, Is.EqualTo("files"));
            Assert.That(request.User, Is.EqualTo("1000:1000"));
        });
    }

    [Test]
    public async Task BuildAndExistingOperationsCarryAuthorizedDeploymentScope()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var scope = new ApplicationContainerScope(appId, targetId);
        var runtime = Runtime(request => request.Operation switch
        {
            HostContainerOperationKinds.StartContainer => new() { Success = true, Running = true, HostAddress = "10.0.0.4", HostPort = 32101, ContainerPort = 8080 },
            HostContainerOperationKinds.InspectContainer => new() { Success = true, Exists = true, Running = true, ContainerId = "full-id", HostPort = 32101, ContainerPort = 8080 },
            HostContainerOperationKinds.GetContainerLogs => new() { Success = true, Logs = "logs" },
            _ => new() { Success = true }
        });

        await runtime.RunBuildAsync(
            new("build:1", new() { Executable = "build" }, @"C:\workspace", 60, new(appId, targetId, attemptId)),
            new Dictionary<string, string>());
        var started = await runtime.StartApplicationAsync(scope, "full-id");
        await runtime.InspectAsync(scope, "full-id");
        await runtime.IsRunningAsync(scope, "full-id");
        await runtime.GetLogsAsync(scope, "full-id");
        await runtime.StopAndRemoveAsync(scope, "full-id");

        Assert.Multiple(() =>
        {
            Assert.That(requests.First().Scope.DeploymentAttemptId, Is.EqualTo(attemptId));
            Assert.That(requests.First().OperationId, Is.EqualTo(attemptId));
            Assert.That(requests.All(x => x.Scope.AppId == appId && x.Scope.DeploymentTargetId == targetId), Is.True);
            Assert.That(requests.Skip(1).All(x => x.ContainerId == "full-id"), Is.True);
            Assert.That(started.HostPort, Is.EqualTo(32101));
        });
    }

    [TestCase(RunnerPurposes.Managed, "Standard", true, true)]
    [TestCase(RunnerPurposes.Managed, "PrivilegedHost", true, false)]
    [TestCase(RunnerPurposes.Managed, RunnerApplicationProfiles.MachineManagement, false, true)]
    [TestCase(RunnerPurposes.Managed, "Standard", false, false)]
    [TestCase("Infrastructure", "Standard", true, false)]
    public void RuntimeSelectionRequiresSupportedManagedBrokerShape(string purpose, string profile, bool hasHostedId, bool expected)
        => Assert.That(RunnerApplicationContainerRuntimeSelection.UsesBroker(
            purpose, profile, hasHostedId ? Guid.NewGuid().ToString() : null), Is.EqualTo(expected));

    [Test]
    public async Task MachineManagementScopeUsesStableRunnerIdentityWithoutHostedBinding()
    {
        var runnerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_ID", runnerId.ToString());
        var runtime = new BrokeredApplicationContainerRuntime(
            (request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new HostContainerExecutionResponse { Success = true });
            },
            () => Task.FromResult<RunnerInstallationProfile?>(new()
            {
                Purpose = RunnerPurposes.Managed,
                ApplicationProfile = RunnerApplicationProfiles.MachineManagement
            }));

        await runtime.RemoveTargetContainersAsync(new(appId, targetId));

        Assert.Multiple(() =>
        {
            Assert.That(requests.Single().Scope.MachineRunnerId, Is.EqualTo(runnerId));
            Assert.That(requests.Single().Scope.HostedRunnerId, Is.EqualTo(Guid.Empty));
            Assert.That(requests.Single().Scope.RunnerHostId, Is.EqualTo(Guid.Empty));
        });
    }

    private BrokeredApplicationContainerRuntime Runtime(Func<HostContainerExecutionRequest, HostContainerExecutionResponse> response)
        => new(
            (request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(response(request));
            },
            () => Task.FromResult<RunnerInstallationProfile?>(new() { Purpose = RunnerPurposes.Managed, ApplicationProfile = RunnerApplicationProfiles.MachineManagement }));
}
