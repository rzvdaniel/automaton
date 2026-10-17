using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class MachineActionContainerIsolationTests
{
    [TestCase(HostContainerOperationKinds.PrepareActionDependencies)]
    [TestCase(HostContainerOperationKinds.ExecuteAction)]
    public async Task ActionPreparationAndExecutionUseBoundedDocker(string operation)
    {
        await using var fixture = await MachineActionContainerFixture.CreateAsync(operation);
        var response = await fixture.RunAsync();

        Assert.That(response.Success, Is.True);
        fixture.AssertDockerArguments(operation);
    }

    [Test]
    public async Task LargeActionInputIsMountedAsAFileInsteadOfDockerArgument()
    {
        await using var fixture = await MachineActionContainerFixture.CreateAsync(HostContainerOperationKinds.ExecuteAction,
            new string('x', 256 * 1024));
        var response = await fixture.RunAsync();

        Assert.That(response.Success, Is.True);
        fixture.AssertInputUsesFileMount();
    }
}

internal sealed class MachineActionContainerFixture : IAsyncDisposable
{
    private readonly string root;
    private readonly string installationId;
    private readonly string operation;
    private readonly HostContainerActionExecutor executor;
    private readonly List<IReadOnlyList<string>> commands = [];
    private readonly Dictionary<string, string?> priorEnvironment = [];

    private MachineActionContainerFixture(string operation, string? input = null)
    {
        this.operation = operation;
        root = Path.Combine(Path.GetTempPath(), "automaton-machine-action-container", Guid.NewGuid().ToString("N"));
        installationId = "sh_" + Guid.NewGuid().ToString("N");
        var runnerId = Guid.NewGuid();
        SetEnvironment("AUTOMATON_WORKSPACE_ROOT", root);
        SetEnvironment("AUTOMATON_RUNNER_INSTALLATION_ID", installationId);

        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        const string actionKey = "bounded-action";
        var workspace = new RunnerWorkspaceService();
        var actionPath = operation == HostContainerOperationKinds.PrepareActionDependencies
            ? workspace.GetAppActionStagingDirectory(appId, targetId, actionKey, executionId)
            : workspace.GetAppActionCurrentDirectory(appId, targetId, actionKey);
        Directory.CreateDirectory(actionPath);
        File.WriteAllText(Path.Combine(actionPath, "action.yaml"), """
            apiVersion: automaton.dev/v1
            kind: Action
            metadata:
              key: bounded-action
            spec:
              runtime: python
              entrypoint: main.py
            """);
        File.WriteAllText(Path.Combine(actionPath, "main.py"), "print('{}')");
        if (operation == HostContainerOperationKinds.PrepareActionDependencies)
            File.WriteAllText(Path.Combine(actionPath, "requirements.txt"), "fixture-package==1.0");

        executor = new HostContainerActionExecutor(ExecuteDockerAsync, workspace);
        Request = new HostContainerExecutionRequest
        {
            Scope = new(Guid.Empty, Guid.Empty, appId, targetId, MachineRunnerId: runnerId),
            OperationId = executionId,
            Operation = operation,
            RequestedImage = "python:3.12-slim",
            ActionKey = actionKey,
            ActionExecutionId = executionId,
            ActionStagingId = operation == HostContainerOperationKinds.PrepareActionDependencies ? executionId : Guid.Empty,
            ActionInputJson = operation == HostContainerOperationKinds.ExecuteAction ? input ?? "{}" : null,
            Environment = new Dictionary<string, string>(),
            ContainerLimits = new(1.5m, 1024, 128),
            SharedMemoryMb = 64,
            DockerLogMaxSizeMb = 20,
            DockerLogMaxFiles = 4,
            Resources = new(HostContainerResourceKinds.Source, HostContainerResourceKinds.Artifact,
                HostContainerResourceKinds.ApplicationData),
            TimeoutSeconds = 60
        };

    }

    private HostContainerExecutionRequest Request { get; }

    public static Task<MachineActionContainerFixture> CreateAsync(string operation)
        => Task.FromResult(new MachineActionContainerFixture(operation));

    public static Task<MachineActionContainerFixture> CreateAsync(string operation, string input)
        => Task.FromResult(new MachineActionContainerFixture(operation, input));

    public Task<HostContainerExecutionResponse> RunAsync()
        => executor.ExecuteAsync(Request, CancellationToken.None);

    private Task<InfrastructureCommandResponse> ExecuteDockerAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = timeout;
        _ = cancellationToken;
        commands.Add(arguments);
        var labels = arguments.Contains("volume", StringComparer.Ordinal)
            ? $$"""{"automaton.managed":"true","automaton.resource":"machine-workspace-volume","automaton.installation-id":"{{installationId}}","automaton.storage-scope":"workspace"}"""
            : "{}";
        return Task.FromResult(new InfrastructureCommandResponse { Success = true, Output = labels });
    }

    public void AssertDockerArguments(string actionOperation)
    {
        var dockerRun = commands.Single(arguments => arguments.Count > 1 && arguments[1] == "run");
        Assert.Multiple(() =>
        {
            Assert.That(dockerRun, Does.Contain("--cpus"));
            Assert.That(dockerRun, Does.Contain("--memory"));
            Assert.That(dockerRun, Does.Contain("--pids-limit"));
            Assert.That(dockerRun, Does.Contain("--cap-drop"));
            Assert.That(dockerRun, Does.Contain("ALL"));
            Assert.That(dockerRun, Does.Not.Contain("--privileged"));
            Assert.That(dockerRun, Does.Not.Contain("/var/run/docker.sock"));
            Assert.That(dockerRun, Does.Not.Contain("--network=host"));
            Assert.That(string.Join(' ', dockerRun), Does.Contain(actionOperation == HostContainerOperationKinds.PrepareActionDependencies
                ? "pip install" : "/automaton/action/main.py"));
        });
    }

    public void AssertInputUsesFileMount()
    {
        var dockerRun = commands.Single(arguments => arguments.Count > 1 && arguments[1] == "run");
        var commandLine = string.Join('\n', dockerRun);
        Assert.Multiple(() =>
        {
            Assert.That(commandLine, Does.Contain("/automaton/input"));
            Assert.That(commandLine, Does.Not.Contain(new string('x', 256 * 1024)));
            Assert.That(commandLine, Does.Contain("cat /automaton/input/input.json"));
        });
    }

    private void SetEnvironment(string key, string? value)
    {
        priorEnvironment[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var pair in priorEnvironment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        return ValueTask.CompletedTask;
    }
}
