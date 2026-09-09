using System.Text.Json;
using System.Text.Json.Nodes;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class HostContainerRuntimeTests
{
    private static readonly string ContainerId = new('a', 64);
    private static readonly string Digest = "example/runtime@sha256:" + new string('b', 64);
    private readonly HostContainerScope scope = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private readonly Dictionary<string, string?> previousEnvironment = new();
    private readonly List<IReadOnlyList<string>> calls = [];

    [SetUp]
    public void SetUp()
    {
        calls.Clear();
        foreach (var pair in new Dictionary<string, string>
        {
            ["AUTOMATON_RUNNER_PURPOSE"] = RunnerPurposes.Infrastructure,
            ["AUTOMATON_RUNNER_CAPABILITIES"] = RunnerCapabilities.HostContainerExecution,
            ["AUTOMATON_RUNTIME_HOST_ADDRESS"] = "172.17.0.1"
        })
        {
            previousEnvironment[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var pair in previousEnvironment)
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
    }

    private HostContainerExecutionRequest CreateRequest() => new()
    {
        Scope = scope, Operation = HostContainerOperationKinds.CreateApplicationContainer,
        CandidateId = Guid.NewGuid(), RequestedImage = Digest, ContainerPort = 8080,
        Resources = new("source", "artifact", "data"),
        Command = new() { Executable = "scripts/serve", WorkingDirectory = "output", Arguments = ["space separated", "--flag"] },
        Storage = new() { DataDirectory = "database", Mounts = [new() { Name = "uploads", RelativePath = "media/files" }] }
    };

    private HostContainerExecutionRequest Operation(string operation, string reference = "candidate-name") => new()
    {
        Scope = scope, Operation = operation, ContainerId = reference
    };

    private HostContainerExecutionService Service(Func<IReadOnlyList<string>, InfrastructureCommandResponse>? execute = null)
        => new((args, _, _) =>
        {
            calls.Add(args.ToArray());
            return Task.FromResult(execute?.Invoke(args) ?? new InfrastructureCommandResponse
            {
                Success = true,
                Output = args[1] == "volume" ? VolumeLabels() : args[1] == "create" ? ContainerId : Inspection().ToJsonString()
            });
        });

    private string VolumeLabels() => JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["automaton.managed"] = "true", ["automaton.resource"] = "hosted-runner-volume",
        ["automaton.hosted-runner-id"] = scope.HostedRunnerId.ToString(), ["automaton.storage-scope"] = "user"
    });

    private JsonObject Inspection(bool running = true, string? port = "49152") => new()
    {
        ["Id"] = ContainerId,
        ["State"] = new JsonObject { ["Running"] = running },
        ["Config"] = new JsonObject
        {
            ["Labels"] = new JsonObject
            {
                ["com.automaton.managed"] = "true", ["com.automaton.runtime-kind"] = "generic",
                ["com.automaton.app-id"] = scope.AppId.ToString(),
                ["com.automaton.deployment-target-id"] = scope.DeploymentTargetId.ToString(),
                ["com.automaton.hosted-runner-id"] = scope.HostedRunnerId.ToString(),
                ["com.automaton.runner-host-id"] = scope.RunnerHostId.ToString()
            }
        },
        ["HostConfig"] = new JsonObject
        {
            ["NetworkMode"] = "bridge", ["PortBindings"] = new JsonObject { ["8080/tcp"] = Bindings("0") }
        },
        ["NetworkSettings"] = new JsonObject
        {
            ["Ports"] = port is null ? new JsonObject() : new JsonObject { ["8080/tcp"] = Bindings(port) }
        }
    };

    private static JsonArray Bindings(string port) => new(new JsonObject { ["HostIp"] = "172.17.0.1", ["HostPort"] = port });

    [Test]
    public async Task CreateUsesOnlyScopedVolumeSubpathsAndSandboxedImmutableCommand()
    {
        var request = CreateRequest();
        request.Command!.Environment["AUTOMATON_PORT"] = "9999";
        request.Command.Environment["MODE"] = "command";
        request = request with { Environment = new Dictionary<string, string> { ["MODE"] = "request" } };
        var result = await Service().ExecuteAsync(request);
        Assert.That(result.Success, Is.True, result.Error);
        var args = calls.Single(x => x[1] == "create").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(result.HostPort, Is.Null);
            Assert.That(calls.Any(x => x[1] is "start" or "pull"), Is.False);
            Assert.That(args[args.IndexOf("--publish") + 1], Is.EqualTo("172.17.0.1::8080"));
            Assert.That(args, Does.Contain("no-new-privileges"));
            Assert.That(args[args.IndexOf("--cap-drop") + 1], Is.EqualTo("ALL"));
            Assert.That(args[args.IndexOf("--network") + 1], Is.EqualTo("bridge"));
            Assert.That(args[args.IndexOf("--workdir") + 1], Is.EqualTo("/automaton/app/output"));
            Assert.That(args[args.IndexOf("--entrypoint") + 1], Is.EqualTo("/automaton/app/scripts/serve"));
            Assert.That(args.TakeLast(3), Is.EqualTo(new[] { Digest, "space separated", "--flag" }));
            Assert.That(args, Does.Contain("MODE=request"));
            Assert.That(args, Does.Contain("AUTOMATON_PORT=8080"));
            Assert.That(args, Does.Contain("AUTOMATON_APP_DATA_DIR=/automaton/data"));
            Assert.That(args, Does.Contain($"AUTOMATON_APP_ID={scope.AppId:D}"));
            Assert.That(args, Does.Contain($"AUTOMATON_DEPLOYMENT_TARGET_ID={scope.DeploymentTargetId:D}"));
            Assert.That(args, Does.Contain("AUTOMATON_STORAGE_UPLOADS=/automaton/data/uploads"));
            Assert.That(string.Join(' ', args), Does.Not.Contain("docker.sock").And.Not.Contain("/host"));
            Assert.That(args, Does.Not.Contain("--privileged").And.Not.Contain("--pid"));
        });
        var mounts = args.Where((_, index) => index > 0 && args[index - 1] == "--mount").ToArray();
        Assert.That(mounts, Has.Length.EqualTo(3));
        Assert.That(mounts[0], Does.Contain($"volume-subpath=workspace/apps/{scope.AppId:D}/targets/{scope.DeploymentTargetId:D}/artifacts/candidates/{request.CandidateId:D}/application"));
        Assert.That(mounts[0], Does.Contain("\"target=/automaton/app\"").And.Contain("\"readonly\""));
        Assert.That(mounts[1], Does.Contain($"volume-subpath=workspace/data/{scope.AppId:D}/database").And.Not.Contain("readonly"));
        Assert.That(mounts[2], Does.Contain($"volume-subpath=workspace/data/{scope.AppId:D}/media/files").And.Not.Contain("readonly"));
        Assert.That(mounts.All(x => x.Contains($"source=automaton-hosted-runner-{scope.HostedRunnerId:N}-user")), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CreateReusesDirectRuntimeCommandSemantics(bool shell)
    {
        var request = CreateRequest();
        request.Command!.Executable = "echo";
        request.Command.UseShell = shell;
        request.Command.Arguments = ["a'b", "$HOME", "two words"];
        Assert.That((await Service().ExecuteAsync(request)).Success, Is.True);
        var args = calls.Last().ToList();
        Assert.That(args[args.IndexOf("--entrypoint") + 1], Is.EqualTo(shell ? "/bin/sh" : "echo"));
        Assert.That(args.Skip(args.IndexOf(Digest) + 1), Is.EqualTo(shell
            ? new[] { "-c", "'echo' 'a'\\''b' '$HOME' 'two words'" }
            : new[] { "a'b", "$HOME", "two words" }));
    }

    [TestCase("app-id")]
    [TestCase("deployment-target-id")]
    [TestCase("hosted-runner-id")]
    [TestCase("runner-host-id")]
    [TestCase("managed")]
    [TestCase("runtime-kind")]
    public async Task EveryOperationRejectsWrongOwnershipBeforeMutationOrLogs(string label)
    {
        foreach (var operation in new[] { HostContainerOperationKinds.StartContainer, HostContainerOperationKinds.InspectContainer,
            HostContainerOperationKinds.GetContainerLogs, HostContainerOperationKinds.RemoveContainer })
        {
            calls.Clear();
            var state = Inspection();
            state["Config"]!["Labels"]![$"com.automaton.{label}"] = "wrong";
            var result = await Service(_ => new() { Success = true, Output = state.ToJsonString() }).ExecuteAsync(Operation(operation));
            Assert.That(result.ErrorCode, Is.EqualTo("container_ownership"), operation);
            Assert.That(calls, Has.Count.EqualTo(1));
        }
    }

    [TestCase(HostContainerOperationKinds.StartContainer, "start")]
    [TestCase(HostContainerOperationKinds.GetContainerLogs, "logs")]
    [TestCase(HostContainerOperationKinds.RemoveContainer, "rm")]
    public async Task OperationsInspectThenUseFullIdInsteadOfCallerName(string operation, string command)
    {
        var result = await Service().ExecuteAsync(Operation(operation));
        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(calls[0], Is.EqualTo(new[] { "docker", "container", "inspect", "--format", "{{json .}}", "candidate-name" }));
        Assert.That(calls[1][1], Is.EqualTo(command));
        Assert.That(calls[1].Last(), Is.EqualTo(ContainerId));
    }

    [Test]
    public async Task StartInspectsBeforeAndAfterStartAndReturnsSecondEndpoint()
    {
        var inspectionCount = 0;
        var result = await Service(args =>
        {
            if (args.Contains("inspect"))
                return new() { Success = true, Output = Inspection(inspectionCount++ == 1, inspectionCount == 1 ? null : "49152").ToJsonString() };
            return new() { Success = true };
        }).ExecuteAsync(Operation(HostContainerOperationKinds.StartContainer));
        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(calls.Select(call => call[1]), Is.EqualTo(new[] { "container", "start", "container" }));
        Assert.That(calls[2].Last(), Is.EqualTo(ContainerId));
        Assert.That(calls[2], Does.Not.Contain("candidate-name"));
        Assert.That(result.Running, Is.True);
        Assert.That(result.HostPort, Is.EqualTo(49152));
    }

    [TestCase(true, "49152")]
    [TestCase(false, "49152")]
    [TestCase(false, null)]
    [TestCase(false, "0")]
    public async Task InspectAcceptsAllocatedOrUnknownPortWithoutAssumingTiming(bool running, string? port)
    {
        var result = await Service(_ => new() { Success = true, Output = Inspection(running, port).ToJsonString() })
            .ExecuteAsync(Operation(HostContainerOperationKinds.InspectContainer));
        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.Exists, Is.True);
        Assert.That(result.Running, Is.EqualTo(running));
        Assert.That(result.HostPort, Is.EqualTo(port is null or "0" ? null : (int?)49152));
        Assert.That(result.HostAddress, Is.EqualTo(result.HostPort.HasValue ? "172.17.0.1" : null));
    }

    [TestCase("not-json")]
    [TestCase("running-string")]
    [TestCase("short-id")]
    [TestCase("public-ip")]
    [TestCase("multiple-bindings")]
    [TestCase("bad-port")]
    [TestCase("missing-port")]
    [TestCase("host-network")]
    [TestCase("extra-publication")]
    [TestCase("duplicate")]
    public async Task InspectRejectsMalformedOrUnsafeState(string kind)
    {
        var state = Inspection();
        switch (kind)
        {
            case "running-string": state["State"]!["Running"] = "true"; break;
            case "short-id": state["Id"] = "abc"; break;
            case "public-ip": state["NetworkSettings"]!["Ports"]!["8080/tcp"]![0]!["HostIp"] = "0.0.0.0"; break;
            case "multiple-bindings": state["NetworkSettings"]!["Ports"]!["8080/tcp"]!.AsArray().Add(new JsonObject()); break;
            case "bad-port": state["NetworkSettings"]!["Ports"]!["8080/tcp"]![0]!["HostPort"] = "65536"; break;
            case "missing-port": state["NetworkSettings"]!["Ports"] = new JsonObject(); break;
            case "host-network": state["HostConfig"]!["NetworkMode"] = "host"; break;
            case "extra-publication": state["NetworkSettings"]!["Ports"]!["9999/tcp"] = Bindings("12345"); break;
        }
        var json = kind == "not-json" ? "not-json" : state.ToJsonString();
        if (kind == "duplicate") json = json.Replace("\"Running\":true", "\"Running\":false,\"Running\":true");
        var result = await Service(_ => new() { Success = true, Output = json })
            .ExecuteAsync(Operation(HostContainerOperationKinds.InspectContainer));
        Assert.That(result.ErrorCode, Is.EqualTo("container_inspect"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task RemoveTreatsOnlyExactNotFoundAsIdempotentSuccess(bool missing)
    {
        var result = await Service(_ => new()
        {
            Error = missing ? "Error: No such container: candidate-name" : "Cannot connect to Docker daemon"
        }).ExecuteAsync(Operation(HostContainerOperationKinds.RemoveContainer));
        Assert.That(result.Success, Is.EqualTo(missing));
        Assert.That(calls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task LogsAreTail200AndBoundedEvenForOneHugeLine()
    {
        var logs = new string('x', 200_000) + "last";
        var result = await Service(args => new()
        {
            Success = true, Output = args[1] == "logs" ? logs : Inspection().ToJsonString()
        }).ExecuteAsync(Operation(HostContainerOperationKinds.GetContainerLogs));
        Assert.That(calls[1], Is.EqualTo(new[] { "docker", "logs", "--tail", "200", ContainerId }));
        Assert.That(result.Logs, Has.Length.EqualTo(65_536));
        Assert.That(result.Logs, Does.EndWith("last"));
        Assert.That(await InfrastructureCommandService.ReadLogTailAsync(new StringReader(logs), CancellationToken.None),
            Is.EqualTo(logs[^65_536..]));
    }

    [TestCase("../another-app")]
    [TestCase("/host")]
    [TestCase("C:\\host")]
    public async Task CreateRejectsUntrustedStoragePathsBeforeDocker(string path)
    {
        var request = CreateRequest();
        request.Storage!.DataDirectory = path;
        var result = await Service().ExecuteAsync(request);
        Assert.That(result.Success, Is.False);
        Assert.That(calls, Is.Empty);
    }

    [TestCase("host-port")]
    [TestCase("mutable-image")]
    [TestCase("no-candidate")]
    [TestCase("bad-volume")]
    public async Task CreateRejectsCallerAuthorityInputs(string kind)
    {
        var request = CreateRequest();
        request = kind switch
        {
            "host-port" => request with { HostPort = 49152 },
            "mutable-image" => request with { RequestedImage = "example/runtime:latest" },
            "no-candidate" => request with { CandidateId = Guid.Empty },
            _ => request with { Resources = new("arbitrary-volume", "artifact", "data") }
        };
        Assert.That((await Service().ExecuteAsync(request)).Success, Is.False);
        Assert.That(calls, Is.Empty);
    }

    [Test]
    public async Task CreateRejectsVolumeWithExtraOrWrongOwnershipLabels()
    {
        foreach (var json in new[] { VolumeLabels().Replace("\"user\"", "\"shared\""), VolumeLabels().Replace("}", ",\"extra\":\"value\"}") })
        {
            calls.Clear();
            var result = await Service(_ => new() { Success = true, Output = json }).ExecuteAsync(CreateRequest());
            Assert.That(result.ErrorCode, Is.EqualTo("trusted_storage"));
            Assert.That(calls, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task TwoCreatesAlwaysRequestDockerAllocation()
    {
        var service = Service();
        await service.ExecuteAsync(CreateRequest());
        await service.ExecuteAsync(CreateRequest());
        var creates = calls.Where(x => x[1] == "create").Select(x => x.ToList()).ToArray();
        Assert.That(creates, Has.Length.EqualTo(2));
        Assert.That(creates.All(x => x[x.IndexOf("--publish") + 1] == "172.17.0.1::8080"), Is.True);
        Assert.That(creates.Select(x => x[x.IndexOf("--name") + 1]).Distinct().Count(), Is.EqualTo(2));
    }

    [Test]
    public void VolumeSubpathsMatchExistingWorkspaceAndStorageModel()
    {
        var workspace = new RunnerWorkspaceService();
        var candidate = Guid.NewGuid();
        var relative = Path.GetRelativePath(workspace.WorkspaceRoot,
            workspace.GetAppRuntimeCandidateApplicationDirectory(scope.AppId, scope.DeploymentTargetId, candidate)).Replace('\\', '/');
        Assert.That(RunnerWorkspaceService.GetRuntimeCandidateVolumeSubpath(scope.AppId, scope.DeploymentTargetId, candidate),
            Is.EqualTo("workspace/" + relative));
        var dataRelative = Path.GetRelativePath(workspace.WorkspaceRoot, workspace.ResolveApplicationDataPath(scope.AppId, "database")).Replace('\\', '/');
        Assert.That(RunnerWorkspaceService.GetApplicationDataVolumeSubpath(scope.AppId, "database"), Is.EqualTo("workspace/" + dataRelative));
    }
}
