using System.Diagnostics;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class MachineActionDependencyPreparationIntegrationTests
{
    [Test]
    public async Task PackageHooksUseTheBoundedDependencyPreparationContainer()
    {
        await using var fixture = new MachineActionDependencyPreparationFixture();
        var results = await fixture.PrepareAsync();

        Assert.That(results.Single().Status, Is.EqualTo("deployed"));
        fixture.AssertBoundedInstallCommand();
    }

    [Test]
    [Platform("Linux")]
    public async Task RealPythonVirtualEnvironmentLinksArePublishedAndVersionedSafely()
    {
        await using var fixture = new MachineActionDependencyPreparationFixture(createVirtualEnvironment: true);
        var results = await fixture.PrepareAsync();

        var current = fixture.CurrentDirectory;
        Assert.Multiple(() =>
        {
            Assert.That(results.Single().Status, Is.EqualTo("deployed"));
            AssertPublishedVirtualEnvironment(current);
            Assert.That(fixture.ListVersions().Versions, Has.Count.EqualTo(1));
        });
        fixture.AssertBoundedInstallCommand();

        fixture.UpdateActionSource();
        Assert.That((await fixture.PrepareAsync()).Single().Status, Is.EqualTo("deployed"));
        var previous = fixture.ListVersions().Versions.Single(version => !version.IsCurrent);
        Assert.That(fixture.RestoreVersion(previous.Version).Success, Is.True);
        AssertPublishedVirtualEnvironment(fixture.CurrentDirectory);
    }

    private static void AssertPublishedVirtualEnvironment(string current)
    {
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(current, ".automaton-action-deployment.json")), Is.True);
            Assert.That(new FileInfo(Path.Combine(current, ".venv", "bin", "python3")).LinkTarget, Is.Not.Null);
            Assert.That(new DirectoryInfo(Path.Combine(current, ".venv", "lib64")).LinkTarget, Is.EqualTo("lib"));
        });
    }

    [Test]
    [Platform("Linux")]
    public async Task DependencyHookSymlinkCannotRedirectMetadataWriteOutsideStaging()
    {
        await using var fixture = new MachineActionDependencyPreparationFixture(createMetadataSymlink: true);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.PrepareAsync());
        Assert.That(await File.ReadAllTextAsync(fixture.SentinelPath), Is.EqualTo("unchanged sentinel"));
    }
}

internal sealed class MachineActionDependencyPreparationFixture : IAsyncDisposable
{
    private const string ActionKey = "dependency-hook";
    private readonly string workspaceRoot = Path.Combine(Path.GetTempPath(), "automaton-action-preparation", Guid.NewGuid().ToString("N"));
    private readonly string installationId = "sh_" + Guid.NewGuid().ToString("N");
    private readonly Guid appId = Guid.NewGuid();
    private readonly Guid targetId = Guid.NewGuid();
    private readonly RunnerWorkspaceService workspace = new();
    private readonly List<IReadOnlyList<string>> commands = [];
    private readonly Dictionary<string, string?> priorEnvironment = [];
    private readonly AppActionDeploymentService deployment;
    private readonly bool createMetadataSymlink;
    private readonly bool createVirtualEnvironment;
    public string CurrentDirectory => workspace.GetAppActionCurrentDirectory(appId, targetId, ActionKey);
    public string SentinelPath { get; } = Path.Combine(Path.GetTempPath(), $"automaton-action-sentinel-{Guid.NewGuid():N}.txt");

    public MachineActionDependencyPreparationFixture(bool createMetadataSymlink = false, bool createVirtualEnvironment = false)
    {
        this.createMetadataSymlink = createMetadataSymlink;
        this.createVirtualEnvironment = createVirtualEnvironment;
        File.WriteAllText(SentinelPath, "unchanged sentinel");
        SetEnvironment("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        SetEnvironment("AUTOMATON_RUNNER_PURPOSE", "Managed");
        SetEnvironment("AUTOMATON_RUNNER_APPLICATION_PROFILE", "MachineManagement");
        SetEnvironment("AUTOMATON_RUNNER_CAPABILITIES", "ManagedContainerExecution");
        SetEnvironment("AUTOMATON_RUNNER_ID", Guid.NewGuid().ToString());
        SetEnvironment("AUTOMATON_RUNNER_INSTALLATION_ID", installationId);
        WriteActionSource();
        deployment = CreateDeploymentService();
    }

    public Task<IReadOnlyList<AppActionDeploymentResult>> PrepareAsync()
        => deployment.PrepareAllAsync(appId, targetId, containerPolicy: new(
            new HostContainerResourceLimits(1.5m, 1024, 128), 64, 20, 5, "python:3.12-slim"));

    public AppActionVersionsResponse ListVersions() => deployment.ListVersions(appId, targetId, ActionKey);

    public void UpdateActionSource()
    {
        var source = Path.Combine(workspace.GetAppSourceRoot(appId, targetId), "actions", ActionKey, "main.py");
        File.WriteAllText(source, "print('{\"updated\":true}')");
    }

    public AppActionVersionOperationResponse RestoreVersion(string version)
        => deployment.RestoreVersion(appId, targetId, ActionKey, version);

    public void AssertBoundedInstallCommand()
    {
        var run = commands.Single(arguments => arguments.Count > 1 && arguments[1] == "run");
        Assert.Multiple(() =>
        {
            Assert.That(commands.All(arguments => arguments[0] == "docker"), Is.True);
            Assert.That(string.Join(' ', run), Does.Contain("pip install"));
            Assert.That(run, Does.Contain("--cpus"));
            Assert.That(run, Does.Contain("--memory"));
            Assert.That(run, Does.Contain("--pids-limit"));
            Assert.That(run, Does.Not.Contain("--privileged"));
            Assert.That(run, Does.Not.Contain("/var/run/docker.sock"));
        });
    }

    private AppActionDeploymentService CreateDeploymentService()
    {
        var container = new HostContainerExecutionService(ExecuteDockerAsync);
        return new AppActionDeploymentService(workspace,
            machineDependencyPreparer: new MachineActionDependencyPreparer(container));
    }

    private async Task<InfrastructureCommandResponse> ExecuteDockerAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _ = timeout;
        _ = cancellationToken;
        commands.Add(arguments);
        if (createMetadataSymlink && arguments.Count > 1 && arguments[1] == "run")
        {
            var actionRoot = workspace.GetAppActionRuntimeRoot(appId, targetId, ActionKey);
            var staging = Directory.GetDirectories(actionRoot, ".staging-*").Single();
            File.CreateSymbolicLink(Path.Combine(staging, ".automaton-action-deployment.json"), SentinelPath);
        }
        if (createVirtualEnvironment && arguments.Count > 1 && arguments[1] == "run")
            await CreateVirtualEnvironmentAsync();
        var output = arguments.Contains("volume", StringComparer.Ordinal) ? CreateVolumeLabels() : "{}";
        return new InfrastructureCommandResponse { Success = true, Output = output };
    }

    private async Task CreateVirtualEnvironmentAsync()
    {
        // Create only Python's standard virtual-environment structure; dependency installation
        // remains represented by the bounded Docker command asserted by this fixture.
        var actionRoot = workspace.GetAppActionRuntimeRoot(appId, targetId, ActionKey);
        var staging = Directory.GetDirectories(actionRoot, ".staging-*").Single();
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "python3",
            ArgumentList = { "-m", "venv", Path.Combine(staging, ".venv") },
            RedirectStandardError = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Could not start python3 to prepare the virtual-environment fixture.");
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Could not create the Python virtual-environment fixture: {error}");
    }

    private string CreateVolumeLabels()
        => $$"""{"automaton.managed":"true","automaton.resource":"machine-workspace-volume","automaton.installation-id":"{{installationId}}","automaton.storage-scope":"workspace"}""";

    private void WriteActionSource()
    {
        var path = Path.Combine(workspace.GetAppSourceRoot(appId, targetId), "actions", ActionKey);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "action.yaml"), """
            apiVersion: automaton.dev/v1
            kind: Action
            metadata:
              key: dependency-hook
            spec:
              runtime: python
              entrypoint: main.py
            """);
        File.WriteAllText(Path.Combine(path, "main.py"), "print('{}')");
        File.WriteAllText(Path.Combine(path, "requirements.txt"), "fixture-package==1.0");
    }

    private void SetEnvironment(string name, string value)
    {
        priorEnvironment[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var pair in priorEnvironment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        if (Directory.Exists(workspaceRoot)) Directory.Delete(workspaceRoot, recursive: true);
        if (File.Exists(SentinelPath)) File.Delete(SentinelPath);
        return ValueTask.CompletedTask;
    }
}
