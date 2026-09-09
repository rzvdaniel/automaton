using System.Diagnostics;
using System.Text.Json;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class HostContainerRuntimeDockerTests
{
    [Test, Explicit("Creates disposable resources on a local Docker Desktop daemon. Requires a cached debian:bookworm-slim image.")]
    public async Task LocalDockerCreateStartInspectLogsAndRemove()
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
        var scope = new HostContainerScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var candidates = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var volume = $"automaton-hosted-runner-{scope.HostedRunnerId:N}-user";
        var artifacts = candidates.Select(candidate => RunnerWorkspaceService.GetRuntimeCandidateVolumeSubpath(scope.AppId, scope.DeploymentTargetId, candidate)).ToArray();
        var data = RunnerWorkspaceService.GetApplicationDataVolumeSubpath(scope.AppId, "store,quoted");
        var environment = new Dictionary<string, string?>();
        var containers = new List<string>();
        var volumeCreated = false;
        var service = new HostContainerExecutionService(new InfrastructureCommandService());
        HostContainerExecutionRequest Operation(string kind, string id) => new() { Scope = scope, Operation = kind, ContainerId = id };
        try
        {
            foreach (var pair in new Dictionary<string, string>
            {
                ["AUTOMATON_RUNNER_PURPOSE"] = RunnerPurposes.Infrastructure,
                ["AUTOMATON_RUNNER_CAPABILITIES"] = RunnerCapabilities.HostContainerExecution,
                ["AUTOMATON_RUNTIME_HOST_ADDRESS"] = "127.0.0.1"
                , ["AUTOMATON_LOCAL_DOCKER_TEST"] = "true"
            })
            {
                environment[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
            await Docker("volume", "create", "--label", "automaton.managed=true",
                "--label", "automaton.resource=hosted-runner-volume", "--label", $"automaton.hosted-runner-id={scope.HostedRunnerId:D}",
                "--label", "automaton.storage-scope=user", volume);
            volumeCreated = true;
            await Docker("run", "--rm", "--network", "none", "--mount", $"type=volume,source={volume},target=/seed",
                "--entrypoint", "mkdir", image, "-p", $"/seed/{artifacts[0]}", $"/seed/{artifacts[1]}", $"/seed/{data}");

            HostContainerExecutionRequest Request(Guid candidate) => new()
            {
                Scope = scope, Operation = HostContainerOperationKinds.CreateApplicationContainer,
                CandidateId = candidate, RequestedImage = image, Resources = new("source", "artifact", "data"),
                ContainerPort = 8080, Storage = new() { DataDirectory = "store,quoted" },
                Command = new()
                {
                    Executable = "sh", Arguments = ["-c", "echo stdout-ok; echo stderr-ok >&2; echo data-ok > /automaton/data/value; if touch /automaton/app/forbidden 2>/dev/null; then echo artifact-writable; else echo artifact-readonly; fi; sleep 120"]
                }
            };
            var createdResults = new List<HostContainerExecutionResponse>();
            foreach (var candidate in candidates)
            {
                var created = await service.ExecuteAsync(Request(candidate));
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
            var starts = await Task.WhenAll(containers.Select(id => service.ExecuteAsync(Operation(HostContainerOperationKinds.StartContainer, id))));
            Assert.That(starts.All(result => result.Success), Is.True);
            Assert.That(starts.All(result => result.Running && result.HostAddress == "127.0.0.1" && result.HostPort > 0), Is.True);
            Assert.That(starts.Select(result => result.HostPort).Distinct().Count(), Is.EqualTo(2));
            TestContext.Out.WriteLine($"Start effective ports: {string.Join(", ", starts.Select(result => result.HostPort))}");
            var inspections = await Task.WhenAll(containers.Select(id => service.ExecuteAsync(Operation(HostContainerOperationKinds.InspectContainer, id))));
            Assert.That(inspections.All(result => result.Success && result.Running && result.HostAddress == "127.0.0.1" && result.HostPort > 0), Is.True);
            Assert.That(inspections.Select(result => result.HostPort).Distinct().Count(), Is.EqualTo(2));
            TestContext.Out.WriteLine($"After start effective ports: {string.Join(", ", inspections.Select(result => result.HostPort))}");

            foreach (var id in containers.ToArray())
            {
                var raw = await Docker("container", "inspect", "--format", "{{json .}}", id);
                using var document = JsonDocument.Parse(raw);
                var mounts = document.RootElement.GetProperty("Mounts").EnumerateArray().ToArray();
                Assert.That(mounts, Has.Length.EqualTo(2));
                Assert.That(mounts.Single(mount => mount.GetProperty("Destination").GetString() == "/automaton/app").GetProperty("RW").GetBoolean(), Is.False);
                Assert.That(mounts.Single(mount => mount.GetProperty("Destination").GetString() == "/automaton/data").GetProperty("RW").GetBoolean(), Is.True);
                var logs = await service.ExecuteAsync(Operation(HostContainerOperationKinds.GetContainerLogs, id));
                Assert.That(logs.Success, Is.True);
                Assert.That(logs.Logs, Does.Contain("stdout-ok").And.Contain("stderr-ok").And.Contain("artifact-readonly"));
                var wrongScope = Operation(HostContainerOperationKinds.RemoveContainer, id) with { Scope = scope with { AppId = Guid.NewGuid() } };
                Assert.That((await service.ExecuteAsync(wrongScope)).ErrorCode, Is.EqualTo("container_ownership"));
                Assert.That((await service.ExecuteAsync(Operation(HostContainerOperationKinds.RemoveContainer, id))).Success, Is.True);
                Assert.That((await service.ExecuteAsync(Operation(HostContainerOperationKinds.RemoveContainer, id))).Success, Is.True);
                containers.Remove(id);
            }
            Assert.That((await Docker("run", "--rm", "--network", "none", "--mount", $"type=volume,source={volume},target=/seed,readonly",
                "--entrypoint", "cat", image, $"/seed/{data}/value")).Trim(), Is.EqualTo("data-ok"));
        }
        finally
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
    }

    private static async Task<string> Docker(params string[] args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("docker")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            }
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
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
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
