using System.Diagnostics;
using System.Text.Json;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

internal sealed class HostContainerRuntimeDockerAcceptance
{
    private const string SeedCommand = "echo stdout-ok; echo stderr-ok >&2; echo data-ok > /automaton/data/value; if touch /automaton/app/forbidden 2>/dev/null; then echo artifact-writable; else echo artifact-readonly; fi; if touch /root/forbidden 2>/dev/null; then echo root-writable; else echo root-readonly; fi; dd if=/dev/zero of=/tmp/bounded.tmp bs=1M count=40 status=none 2>/dev/null && echo scratch-overflow-success || echo scratch-overflow-blocked; rm -f /tmp/bounded.tmp; echo scratch-ok >/tmp/scratch && echo scratch-writable; sleep 120";
    private readonly HostContainerScope scope = new(Guid.Empty, Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), MachineRunnerId: Guid.NewGuid());
    private readonly string installationId = "sh_" + Guid.NewGuid().ToString("N");
    private readonly Guid[] candidates = [Guid.NewGuid(), Guid.NewGuid()];
    private readonly string volume;
    private readonly string[] artifacts;
    private readonly string data;
    private readonly Dictionary<string, string?> environment = new();
    private readonly List<string> containers = [];
    private readonly HostContainerExecutionService service = new(new InfrastructureCommandService());
    private bool volumeCreated;

    private HostContainerRuntimeDockerAcceptance()
    {
        volume = $"automaton-machine-runner-{installationId}-workspace";
        artifacts = candidates.Select(candidate => RunnerWorkspaceService.GetRuntimeCandidateVolumeSubpath(
            scope.AppId, scope.DeploymentTargetId, candidate)).ToArray();
        data = RunnerWorkspaceService.GetApplicationDataVolumeSubpath(scope.AppId, "store,quoted");
    }

    public static async Task RunAsync()
    {
        var scenario = new HostContainerRuntimeDockerAcceptance();
        var image = await scenario.VerifyLocalDockerTargetAsync();
        try
        {
            scenario.ConfigureRunnerEnvironment();
            await scenario.CreateStorageVolumeAsync(image);
            await scenario.CreateContainersAsync(image);
            await scenario.StartAndInspectContainersAsync();
            await scenario.VerifyMountsLogsAndRemovalAsync();
            await scenario.VerifyPersistentDataAsync(image);
            await scenario.VerifyBuildContainerBoundariesAsync(image);
        }
        finally
        {
            await scenario.CleanupAsync();
        }
    }

    private async Task<string> VerifyLocalDockerTargetAsync()
    {
        Assert.That(Environment.GetEnvironmentVariable("DOCKER_HOST"), Is.Null.Or.Empty);
        Assert.That(Environment.GetEnvironmentVariable("DOCKER_CONTEXT"), Is.Null.Or.Empty);
        var context = (await Docker("context", "show")).Trim();
        Assert.That(context, Is.EqualTo("desktop-linux"), "This local-only test must not target a production Docker context.");
        var endpoint = (await Docker("context", "inspect", "--format", "{{.Endpoints.docker.Host}}", context)).Trim();
        Assert.That(endpoint, Does.StartWith("npipe://"), "This test requires the local Docker Desktop named pipe.");
        TestContext.Out.WriteLine(await Docker("version", "--format", "{{.Server.Version}}"));
        var image = (await Docker("image", "inspect", "--format", "{{index .RepoDigests 0}}", "debian:bookworm-slim")).Trim();
        Assert.That(HostContainerRuntimeValidation.IsRepoDigest(image), Is.True);
        return image;
    }

    private void ConfigureRunnerEnvironment()
    {
        foreach (var pair in new Dictionary<string, string>
        {
            ["AUTOMATON_RUNNER_PURPOSE"] = RunnerPurposes.Managed,
            ["AUTOMATON_RUNNER_CAPABILITIES"] = RunnerCapabilities.ManagedContainerExecution,
            ["AUTOMATON_RUNTIME_HOST_ADDRESS"] = "127.0.0.1",
            ["AUTOMATON_LOCAL_DOCKER_TEST"] = "true",
            ["AUTOMATON_RUNNER_APPLICATION_PROFILE"] = RunnerApplicationProfiles.MachineManagement,
            ["AUTOMATON_RUNNER_ID"] = scope.MachineRunnerId.ToString(),
            ["AUTOMATON_RUNNER_INSTALLATION_ID"] = installationId
        })
        {
            environment[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    private async Task CreateStorageVolumeAsync(string image)
    {
        await Docker(
            "volume", "create", "--label", "automaton.managed=true",
            "--label", "automaton.resource=machine-workspace-volume",
            "--label", $"automaton.installation-id={installationId}",
            "--label", "automaton.storage-scope=workspace", volume);
        volumeCreated = true;
        await Docker(
            "run", "--rm", "--network", "none", "--mount", $"type=volume,source={volume},target=/seed",
            "--entrypoint", "mkdir", image, "-p", $"/seed/{artifacts[0]}", $"/seed/{artifacts[1]}", $"/seed/{data}");
        await Docker(
            "run", "--rm", "--network", "none", "--mount", $"type=volume,source={volume},target=/seed",
            "--entrypoint", "chmod", image, "777", $"/seed/{data}");
    }

    private async Task CreateContainersAsync(string image)
    {
        var createdResults = new List<HostContainerExecutionResponse>();
        foreach (var candidate in candidates)
        {
            var created = await service.ExecuteAsync(CreateRequest(candidate, image));
            Assert.That(created.Success, Is.True, $"{created.ErrorCode}: {created.Error}");
            createdResults.Add(created);
            containers.Add(created.ContainerId);
            var before = await service.ExecuteAsync(Operation(HostContainerOperationKinds.InspectContainer, created.ContainerId));
            Assert.That(before.Success, Is.True, before.Error);
            Assert.That(before.Running, Is.False);
            TestContext.Out.WriteLine($"Before start effective port: {before.HostPort?.ToString() ?? "unknown"}");
        }

        Assert.That(createdResults[0].ContainerId, Is.Not.EqualTo(createdResults[1].ContainerId));
        Assert.That(createdResults[0].ContainerName, Is.Not.EqualTo(createdResults[1].ContainerName));
    }

    private HostContainerExecutionRequest CreateRequest(Guid candidate, string image)
        => new()
        {
            Scope = scope,
            Operation = HostContainerOperationKinds.CreateApplicationContainer,
            CandidateId = candidate,
            RequestedImage = image,
            Resources = new("source", "artifact", "data"),
            SharedMemoryMb = 64, DockerLogMaxSizeMb = 10, DockerLogMaxFiles = 3,
                ContainerLimits = new(1m, 1024, 256), MinimumFreeDiskMb = 256,
            ContainerPort = 8080,
            Storage = new() { DataDirectory = "store,quoted" },
            Command = new() { Executable = "sh", Arguments = ["-c", SeedCommand] }
        };

    private async Task StartAndInspectContainersAsync()
    {
        var starts = await Task.WhenAll(containers.Select(id =>
            service.ExecuteAsync(Operation(HostContainerOperationKinds.StartContainer, id))));
        Assert.That(starts.All(result => result.Success), Is.True);
        Assert.That(starts.All(result => result.Running && result.HostAddress == "127.0.0.1" && result.HostPort > 0), Is.True);
        Assert.That(starts.Select(result => result.HostPort).Distinct().Count(), Is.EqualTo(2));
        TestContext.Out.WriteLine($"Start effective ports: {string.Join(", ", starts.Select(result => result.HostPort))}");

        var inspections = await Task.WhenAll(containers.Select(id =>
            service.ExecuteAsync(Operation(HostContainerOperationKinds.InspectContainer, id))));
        Assert.That(inspections.All(result =>
            result.Success && result.Running && result.HostAddress == "127.0.0.1" && result.HostPort > 0), Is.True);
        Assert.That(inspections.Select(result => result.HostPort).Distinct().Count(), Is.EqualTo(2));
        TestContext.Out.WriteLine($"After start effective ports: {string.Join(", ", inspections.Select(result => result.HostPort))}");
    }

    private async Task VerifyMountsLogsAndRemovalAsync()
    {
        foreach (var id in containers.ToArray())
        {
            await VerifyContainerMountsAsync(id);
            var logs = await service.ExecuteAsync(Operation(HostContainerOperationKinds.GetContainerLogs, id));
            Assert.That(logs.Success, Is.True);
            Assert.That(logs.Logs, Does.Contain("stdout-ok").And.Contain("stderr-ok").And.Contain("artifact-readonly").And.Contain("root-readonly").And.Contain("scratch-overflow-blocked").And.Contain("scratch-writable"));
            var wrongScope = Operation(HostContainerOperationKinds.RemoveContainer, id) with
            {
                Scope = scope with { AppId = Guid.NewGuid() }
            };
            Assert.That((await service.ExecuteAsync(wrongScope)).ErrorCode, Is.EqualTo("container_ownership"));
            Assert.That((await service.ExecuteAsync(Operation(HostContainerOperationKinds.RemoveContainer, id))).Success, Is.True);
            Assert.That((await service.ExecuteAsync(Operation(HostContainerOperationKinds.RemoveContainer, id))).Success, Is.True);
            containers.Remove(id);
        }
    }

    private async Task VerifyContainerMountsAsync(string id)
    {
        using var document = JsonDocument.Parse(await Docker("container", "inspect", "--format", "{{json .}}", id));
        var root = document.RootElement;
        var mounts = root.GetProperty("Mounts").EnumerateArray().ToArray();
        Assert.That(mounts.Length, Is.GreaterThanOrEqualTo(2));
        Assert.That(root.GetProperty("HostConfig").GetProperty("ReadonlyRootfs").GetBoolean(), Is.True);
        Assert.That(root.GetProperty("HostConfig").GetProperty("CgroupParent").GetString(), Is.Null.Or.Empty);
        Assert.That(root.GetProperty("HostConfig").GetProperty("ShmSize").GetInt64(), Is.EqualTo(64L * 1024 * 1024));
        Assert.That(root.GetProperty("HostConfig").GetProperty("Tmpfs").GetProperty("/tmp").GetString(), Does.Contain("size=32m"));
        var logConfig = root.GetProperty("HostConfig").GetProperty("LogConfig");
        Assert.That(logConfig.GetProperty("Type").GetString(), Is.EqualTo("local"));
        Assert.That(logConfig.GetProperty("Config").GetProperty("max-size").GetString(), Is.EqualTo("10m"));
        Assert.That(logConfig.GetProperty("Config").GetProperty("max-file").GetString(), Is.EqualTo("3"));
        Assert.That(mounts.Single(mount => mount.GetProperty("Destination").GetString() == "/automaton/app")
            .GetProperty("RW").GetBoolean(), Is.False);
        Assert.That(mounts.Single(mount => mount.GetProperty("Destination").GetString() == "/automaton/data")
            .GetProperty("RW").GetBoolean(), Is.True);
    }

    private async Task VerifyPersistentDataAsync(string image)
    {
        var content = (await Docker(
            "run", "--rm", "--network", "none", "--mount", $"type=volume,source={volume},target=/seed,readonly",
            "--entrypoint", "cat", image, $"/seed/{data}/value")).Trim();
        Assert.That(content, Is.EqualTo("data-ok"));
    }

    private async Task VerifyBuildContainerBoundariesAsync(string image)
    {
        var attemptId = Guid.NewGuid();
        var sourcePath = RunnerWorkspaceService.GetDeploymentStagingVolumeSubpath(
            scope.AppId, scope.DeploymentTargetId, attemptId) + "/source";
        await Docker(
            "run", "--rm", "--network", "none", "--mount", $"type=volume,source={volume},target=/seed",
            "--entrypoint", "mkdir", image, "-p", $"/seed/{sourcePath}");

        var result = await service.ExecuteAsync(new HostContainerExecutionRequest
        {
            Scope = scope with { DeploymentAttemptId = attemptId },
            Operation = HostContainerOperationKinds.RunBuild,
            RequestedImage = image,
            Resources = new("source", "artifact", "data"),
            SharedMemoryMb = 64,
            DockerLogMaxSizeMb = 10,
            DockerLogMaxFiles = 3,
                ContainerLimits = new(1m, 1024, 256), MinimumFreeDiskMb = 256,
            TimeoutSeconds = 60,
            Command = new ApplicationCommandSpec
            {
                Executable = "sh",
                Arguments = ["-ec", "printf build-output > /automaton/source/output; mkdir -p \"$HOME/.cache\"; printf build-cache > \"$HOME/.cache/marker\"; if touch /root/forbidden 2>/dev/null; then exit 10; fi; dd if=/dev/zero of=/tmp/overflow bs=1M count=40 status=none 2>/dev/null && exit 11 || echo scratch-bounded; rm -f /tmp/overflow"]
            }
        });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.ContainerName, Is.Not.Empty);
        var remainingContainers = await Docker(
            "container", "ls", "--all", "--quiet", "--filter", $"name={result.ContainerName}");
        Assert.That(remainingContainers, Is.Empty);
        var content = await Docker(
            "run", "--rm", "--network", "none", "--mount", $"type=volume,source={volume},target=/source,readonly",
            "--entrypoint", "sh", image, "-ec", "cat /source/" + sourcePath + "/output; cat /source/" + sourcePath + "/.automaton-home/.cache/marker");
        Assert.That(content.Replace("\r", "", StringComparison.Ordinal), Is.EqualTo("build-outputbuild-cache"));
    }

    private async Task CleanupAsync()
    {
        var cleanupErrors = new List<string>();
        foreach (var id in containers)
        {
            try { await Docker("rm", "-f", id); }
            catch (Exception ex) { cleanupErrors.Add(ex.Message); }
        }

        if (volumeCreated)
        {
            try { await Docker("volume", "rm", volume); }
            catch (Exception ex) { cleanupErrors.Add(ex.Message); }
        }

        foreach (var pair in environment)
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        Assert.That(cleanupErrors, Is.Empty);
    }

    private HostContainerExecutionRequest Operation(string kind, string id)
        => new() { Scope = scope, Operation = kind, ContainerId = id };

    private static async Task<string> Docker(params string[] args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("docker")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var error = await stderr;
            Assert.That(process.ExitCode, Is.Zero, error);
            return await stdout;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
