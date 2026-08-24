using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class RunnerRuntimeProcessManagerTests
{
    private string root = null!;
    private string? originalRoot;

    [SetUp]
    public void SetUp()
    {
        originalRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        root = Path.Combine(Path.GetTempPath(), "automaton-runtime-tests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalRoot);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    [Test]
    public async Task StructuredRuntime_UsesArgumentsEnvironmentPortAndCapturesBothStreams()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(workspace, registry);

        await manager.ApplyAsync(appId, targetId, CreateContract(["--tcp", "first", "second"]));
        await Task.Delay(150);

        var targetRoot = workspace.GetDeploymentTargetRoot(appId, targetId);
        var metadata = JsonSerializer.Deserialize<RunnerRuntimeDeploymentMetadata>(
            await File.ReadAllTextAsync(Path.Combine(targetRoot, "deployment.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var runtimeRoot = metadata.ArtifactRoot!;
        var stdout = await ReadSharedTextAsync(Path.Combine(runtimeRoot, "logs", "runtime.stdout.log"));
        var stderr = await ReadSharedTextAsync(Path.Combine(runtimeRoot, "logs", "runtime.stderr.log"));

        Assert.Multiple(() =>
        {
            Assert.That(registry.TryGet(appId, targetId, out var registration), Is.True);
            Assert.That(registration.Port, Is.EqualTo(metadata.EffectivePort));
            Assert.That(stdout, Does.Contain("stdout:--tcp|first|second:expected"));
            Assert.That(stderr, Does.Contain("stderr:fixture"));
            Assert.That(metadata.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(metadata.ProcessId, Is.Not.Null);
            Assert.That(File.ReadAllText(Path.Combine(targetRoot, "deployment.json")), Does.Not.Contain("backendModule"));
        });

        await manager.StopAsync(appId, targetId);
        Assert.That(registry.TryGet(appId, targetId, out _), Is.False);
    }

    [Test]
    public async Task TcpReadiness_DoesNotRequireHealthPath()
    {
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(new RunnerWorkspaceService(), registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        await manager.ApplyAsync(appId, targetId, CreateContract(["--tcp"]));

        Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
        await manager.StopAsync(appId, targetId);
    }

    [Test]
    public async Task HttpReadiness_UsesConfiguredHealthPathAndPort()
    {
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(new RunnerWorkspaceService(), registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contract = CreateContract(["--http"]);
        contract.Health = new ApplicationHealthSpec { Path = "/ready", StartupTimeoutSeconds = 5 };

        await manager.ApplyAsync(appId, targetId, contract);

        Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
        await manager.StopAsync(appId, targetId);
    }

    [Test]
    public async Task HttpReadiness_RetriesNonSuccessResponsesUntilSuccess()
    {
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(new RunnerWorkspaceService(), registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contract = CreateContract(["--http-503-once"]);
        contract.Health = new ApplicationHealthSpec { Path = "/ready", StartupTimeoutSeconds = 5 };

        await manager.ApplyAsync(appId, targetId, contract);

        Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
        await manager.StopAsync(appId, targetId);
    }

    [Test]
    public void HttpReadiness_TimesOutWhenResponsesNeverSucceed()
    {
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(new RunnerWorkspaceService(), registry);
        var contract = CreateContract(["--http-503"]);
        contract.Health = new ApplicationHealthSpec { Path = "/ready", StartupTimeoutSeconds = 1 };

        Assert.That(
            async () => await manager.ApplyAsync(Guid.NewGuid(), Guid.NewGuid(), contract),
            Throws.TypeOf<TimeoutException>());
    }

    [Test]
    public async Task ApplyingUpdatedRuntime_ReplacesTheProcessAfterReadiness()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        using var manager = CreateManager(workspace, registry);

        await manager.ApplyAsync(appId, targetId, CreateContract(["--tcp", "old"]));
        var metadataPath = Path.Combine(workspace.GetDeploymentTargetRoot(appId, targetId), "deployment.json");
        var first = JsonSerializer.Deserialize<RunnerRuntimeDeploymentMetadata>(
            await File.ReadAllTextAsync(metadataPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        await manager.ApplyAsync(appId, targetId, CreateContract(["--tcp", "new"]));
        var second = JsonSerializer.Deserialize<RunnerRuntimeDeploymentMetadata>(
            await File.ReadAllTextAsync(metadataPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Multiple(() =>
        {
            Assert.That(first.ProcessId, Is.Not.Null);
            Assert.That(second.ProcessId, Is.Not.EqualTo(first.ProcessId));
            Assert.That(second.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
        });

        await manager.StopAsync(appId, targetId);
    }

    [Test]
    public async Task FailedReplacement_LeavesHealthyRuntimeRoutedAndTracked()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        using var manager = CreateManager(workspace, registry);

        await manager.ApplyAsync(appId, targetId, CreateContract(["--tcp", "original"]));
        var metadataPath = Path.Combine(workspace.GetDeploymentTargetRoot(appId, targetId), "deployment.json");
        var original = ReadMetadata(metadataPath);
        Assert.That(original.ProcessId, Is.Not.Null);
        Assert.That(registry.TryGet(appId, targetId, out var originalRoute), Is.True);

        var replacement = CreateContract([]);
        replacement.Runtime.Command!.Arguments = ["missing-runtime-fixture.dll"];
        replacement.Health.StartupTimeoutSeconds = 1;
        Assert.That(
            async () => await manager.ApplyAsync(appId, targetId, replacement),
            Throws.TypeOf<TimeoutException>());

        var restored = ReadMetadata(metadataPath);
        Assert.Multiple(() =>
        {
            Assert.That(IsProcessAlive(original.ProcessId!.Value), Is.True);
            Assert.That(registry.TryGet(appId, targetId, out var route), Is.True);
            Assert.That(route.Port, Is.EqualTo(originalRoute.Port));
            Assert.That(restored.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(restored.ProcessId, Is.EqualTo(original.ProcessId));
        });

        await manager.StopAsync(appId, targetId);
        Assert.That(await WaitForProcessExitAsync(original.ProcessId!.Value), Is.True);
    }

    [Test]
    public async Task DeployedRuntimeFixture_StartsFromTheGenericApplicationArtifactRoot()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var processManager = CreateManager(workspace, registry);
        var deployment = new RunnerDeploymentService(
            workspace,
            processManager,
            new AppActionDeploymentService(workspace));
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var packagePath = CreateRuntimePackage(appId);

        try
        {
            var result = await deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = packagePath,
                Clean = true
            });

            var metadataPath = Path.Combine(workspace.GetDeploymentTargetRoot(appId, targetId), "deployment.json");
            var deployedMetadata = ReadMetadata(metadataPath);
            var logPath = Path.Combine(
                deployedMetadata.ArtifactRoot!,
                "logs",
                "runtime.stdout.log");
            var output = await WaitForLogLineAsync(logPath, "deployed");
            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True, result.Message);
                Assert.That(output, Does.Contain("stdout:--tcp|deployed"));
                Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
            });
        }
        finally
        {
            await processManager.StopAsync(appId, targetId);
            processManager.Dispose();
            if (File.Exists(packagePath))
                File.Delete(packagePath);
        }
    }

    [Test]
    public async Task FailedDeployment_LeavesCurrentArtifactFilesAndMetadataIntact()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var processManager = CreateManager(workspace, registry);
        var deployment = new RunnerDeploymentService(
            workspace,
            processManager,
            new AppActionDeploymentService(workspace));
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var originalPackage = CreateRuntimePackage(appId, ["RunnerRuntimeFixture.dll", "--tcp", "original"], "current");
        var failedPackage = CreateRuntimePackage(appId, ["missing-runtime-fixture.dll"], "candidate");

        try
        {
            var originalResult = await deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = originalPackage,
                Clean = true
            });
            Assert.That(originalResult.Success, Is.True, originalResult.Message);

            var metadataPath = Path.Combine(workspace.GetDeploymentTargetRoot(appId, targetId), "deployment.json");
            var original = ReadMetadata(metadataPath);
            var originalMarker = Path.Combine(original.ArtifactRoot!, "marker.txt");
            var originalContent = await File.ReadAllTextAsync(originalMarker);
            Assert.That(originalContent, Is.EqualTo("current"));
            Assert.That(registry.TryGet(appId, targetId, out var originalRoute), Is.True);

            var failedResult = await deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = failedPackage,
                Clean = true
            });

            var restored = ReadMetadata(metadataPath);
            Assert.Multiple(() =>
            {
                Assert.That(failedResult.Success, Is.False);
                Assert.That(IsProcessAlive(original.ProcessId!.Value), Is.True);
                Assert.That(registry.TryGet(appId, targetId, out var route), Is.True);
                Assert.That(route.Port, Is.EqualTo(originalRoute.Port));
                Assert.That(restored.State, Is.EqualTo(RuntimeReadinessState.Ready));
                Assert.That(restored.ProcessId, Is.EqualTo(original.ProcessId));
                Assert.That(restored.ArtifactRoot, Is.EqualTo(original.ArtifactRoot));
            });
            Assert.That(await File.ReadAllTextAsync(originalMarker), Is.EqualTo(originalContent));
        }
        finally
        {
            await processManager.StopAsync(appId, targetId);
            processManager.Dispose();
            if (File.Exists(originalPackage)) File.Delete(originalPackage);
            if (File.Exists(failedPackage)) File.Delete(failedPackage);
        }
    }

    [Test]
    public async Task FailedBuild_LeavesCurrentRuntimeAndArtifactsIntact()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var processManager = CreateManager(workspace, registry);
        var deployment = new RunnerDeploymentService(
            workspace,
            processManager,
            new AppActionDeploymentService(workspace));
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var originalPackage = CreateRuntimePackage(
            appId,
            ["RunnerRuntimeFixture.dll", "--tcp", "original"],
            "current");
        var failedPackage = CreateRuntimePackage(
            appId,
            ["RunnerRuntimeFixture.dll", "--tcp", "candidate"],
            "candidate",
            new ApplicationBuildSpec
            {
                Command = new ApplicationCommandSpec
                {
                    Executable = "dotnet",
                    Arguments = ["--not-a-real-build-option"],
                    WorkingDirectory = "."
                }
            });

        try
        {
            var originalResult = await deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = originalPackage,
                Clean = true
            });
            Assert.That(originalResult.Success, Is.True, originalResult.Message);

            var metadataPath = Path.Combine(workspace.GetDeploymentTargetRoot(appId, targetId), "deployment.json");
            var original = ReadMetadata(metadataPath);
            var originalMarker = Path.Combine(original.ArtifactRoot!, "marker.txt");
            var originalContent = await File.ReadAllTextAsync(originalMarker);
            Assert.That(registry.TryGet(appId, targetId, out var originalRoute), Is.True);

            var failedResult = await deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = failedPackage,
                Clean = true
            });

            var restored = ReadMetadata(metadataPath);
            Assert.Multiple(() =>
            {
                Assert.That(failedResult.Success, Is.False);
                Assert.That(failedResult.Message, Does.Contain("Application build failed"));
                Assert.That(IsProcessAlive(original.ProcessId!.Value), Is.True);
                Assert.That(registry.TryGet(appId, targetId, out var route), Is.True);
                Assert.That(route.Port, Is.EqualTo(originalRoute.Port));
                Assert.That(restored.State, Is.EqualTo(RuntimeReadinessState.Ready));
                Assert.That(restored.ProcessId, Is.EqualTo(original.ProcessId));
                Assert.That(restored.ArtifactRoot, Is.EqualTo(original.ArtifactRoot));
            });
            Assert.That(await File.ReadAllTextAsync(originalMarker), Is.EqualTo(originalContent));
        }
        finally
        {
            await processManager.StopAsync(appId, targetId);
            processManager.Dispose();
            if (File.Exists(originalPackage)) File.Delete(originalPackage);
            if (File.Exists(failedPackage)) File.Delete(failedPackage);
        }
    }

    [Test]
    public async Task FinalMetadataCompactionFailure_RetainsPromotedRuntimeForRestartReconciliation()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var failFinalMetadataWrite = false;
        var manager = CreateManager(
            workspace,
            registry,
            metadata => failFinalMetadataWrite
                && metadata.Candidate is null
                && metadata.State == RuntimeReadinessState.Ready
                    ? new IOException("simulated final metadata failure")
                    : null);

        await manager.ApplyAsync(appId, targetId, CreateContract(["--tcp", "original"]));
        var metadataPath = Path.Combine(workspace.GetDeploymentTargetRoot(appId, targetId), "deployment.json");
        var original = ReadMetadata(metadataPath);
        var originalArtifactRoot = original.ArtifactRoot!;

        failFinalMetadataWrite = true;
        var result = await manager.ApplyAsync(appId, targetId, CreateContract(["--tcp", "replacement"]));
        var pending = ReadMetadata(metadataPath);
        var candidateArtifactRoot = pending.Candidate!.ArtifactRoot!;

        Assert.Multiple(() =>
        {
            Assert.That(result.PromotionIrreversible, Is.True);
            Assert.That(result.Warning, Is.Not.Null);
            Assert.That(IsProcessAlive(pending.Candidate.ProcessId!.Value), Is.True);
            Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
            Assert.That(Directory.Exists(candidateArtifactRoot), Is.True);
            Assert.That(Directory.Exists(originalArtifactRoot), Is.False);
        });

        manager.Dispose();
        failFinalMetadataWrite = false;
        using var recoveryManager = CreateManager(workspace, registry);
        await recoveryManager.StartAsync(CancellationToken.None);

        var recovered = ReadMetadata(metadataPath);
        Assert.Multiple(() =>
        {
            Assert.That(recovered.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(recovered.Candidate, Is.Null);
            Assert.That(recovered.ArtifactRoot, Is.EqualTo(candidateArtifactRoot));
            Assert.That(IsProcessAlive(recovered.ProcessId!.Value), Is.True);
            Assert.That(Directory.Exists(candidateArtifactRoot), Is.True);
            Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
        });

        await recoveryManager.StopAsync(appId, targetId);
    }

    [Test]
    public async Task StopWithCancelledShutdownToken_KillsTheEntireProcessTree()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(workspace, registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        await manager.ApplyAsync(appId, targetId, CreateContract(["--spawn-child"]));
        var metadataPath = Path.Combine(workspace.GetDeploymentTargetRoot(appId, targetId), "deployment.json");
        var metadata = ReadMetadata(metadataPath);
        var outputPath = Path.Combine(
            metadata.ArtifactRoot!,
            "logs",
            "runtime.stdout.log");
        var output = await WaitForLogLineAsync(outputPath, "child:");
        var childMarker = output.LastIndexOf("child:", StringComparison.Ordinal);
        var childPid = int.Parse(output[(childMarker + "child:".Length)..].Split('\r', '\n')[0]);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await manager.StopAsync(cancelled.Token);

        Assert.Multiple(() =>
        {
            Assert.That(IsProcessAlive(metadata.ProcessId!.Value), Is.False);
            Assert.That(IsProcessAlive(childPid), Is.False);
        });
    }

    [Test]
    public async Task RecoveryWithPersistedCandidate_FencesBothProcessesAndRebuildsDesiredRuntime()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var currentRoot = workspace.GetAppRuntimeCandidateApplicationDirectory(appId, targetId, Guid.NewGuid());
        var candidateRoot = workspace.GetAppRuntimeCandidateApplicationDirectory(appId, targetId, Guid.NewGuid());
        Directory.CreateDirectory(currentRoot);
        Directory.CreateDirectory(candidateRoot);
        var currentContract = CreateContract(["--tcp", "current"]);
        var candidateContract = CreateContract(["--tcp", "candidate"]);
        var currentPort = 30_000 + Random.Shared.Next(500);
        var candidatePort = currentPort + 1;
        using var currentProcess = StartFixtureProcess(currentRoot, currentPort, "current");
        using var candidateProcess = StartFixtureProcess(candidateRoot, candidatePort, "candidate");
        await Task.Delay(250);

        var currentRecord = new RunnerRuntimeDeploymentMetadata
        {
            AppId = appId.ToString(),
            DeploymentTargetId = targetId.ToString(),
            Runtime = currentContract.Runtime,
            Health = currentContract.Health,
            EffectivePort = currentPort,
            ProcessId = currentProcess.Id,
            ProcessStartTimeUtc = currentProcess.StartTime.ToUniversalTime(),
            ArtifactRoot = currentRoot,
            ArtifactId = "current",
            State = RuntimeReadinessState.Ready
        };
        var metadata = new RunnerRuntimeDeploymentMetadata
        {
            AppId = appId.ToString(),
            DeploymentTargetId = targetId.ToString(),
            Runtime = currentRecord.Runtime,
            Health = currentRecord.Health,
            EffectivePort = currentRecord.EffectivePort,
            ProcessId = currentRecord.ProcessId,
            ProcessStartTimeUtc = currentRecord.ProcessStartTimeUtc,
            ArtifactRoot = currentRecord.ArtifactRoot,
            ArtifactId = currentRecord.ArtifactId,
            State = RuntimeReadinessState.Ready,
            Candidate = new RunnerRuntimeDeploymentMetadata
            {
                AppId = appId.ToString(),
                DeploymentTargetId = targetId.ToString(),
                Runtime = candidateContract.Runtime,
                Health = candidateContract.Health,
                EffectivePort = candidatePort,
                ProcessId = candidateProcess.Id,
                ProcessStartTimeUtc = candidateProcess.StartTime.ToUniversalTime(),
                ArtifactRoot = candidateRoot,
                ArtifactId = "candidate",
                State = RuntimeReadinessState.Starting
            }
        };
        var metadataPath = Path.Combine(workspace.GetDeploymentTargetRoot(appId, targetId), "deployment.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata));

        using var recoveryManager = CreateManager(workspace, registry);
        await recoveryManager.StartAsync(CancellationToken.None);

        var recovered = ReadMetadata(metadataPath);
        Assert.Multiple(() =>
        {
            Assert.That(IsProcessAlive(currentProcess.Id), Is.False);
            Assert.That(IsProcessAlive(candidateProcess.Id), Is.False);
            Assert.That(recovered.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(recovered.Candidate, Is.Null);
            Assert.That(recovered.ArtifactRoot, Is.EqualTo(candidateRoot));
            Assert.That(recovered.ProcessId, Is.Not.EqualTo(currentProcess.Id));
            Assert.That(recovered.ProcessId, Is.Not.EqualTo(candidateProcess.Id));
            Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
        });

        await recoveryManager.StopAsync(appId, targetId);
    }

    [Test]
    public async Task RecoveryWithFirstDeploymentCandidate_ReusesCandidateWithoutCurrentRuntime()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var candidateRoot = workspace.GetAppRuntimeCandidateApplicationDirectory(appId, targetId, Guid.NewGuid());
        Directory.CreateDirectory(candidateRoot);
        var candidateContract = CreateContract(["--tcp", "first-deployment"]);
        var candidatePort = 30_000 + Random.Shared.Next(500);
        candidateContract.Runtime.Port = candidatePort;
        using var candidateProcess = StartFixtureProcess(candidateRoot, candidatePort, "first-deployment");
        await Task.Delay(250);

        var candidateRecord = new RunnerRuntimeDeploymentMetadata
        {
            AppId = appId.ToString(),
            DeploymentTargetId = targetId.ToString(),
            Runtime = candidateContract.Runtime,
            Health = candidateContract.Health,
            EffectivePort = candidatePort,
            ProcessId = candidateProcess.Id,
            ProcessStartTimeUtc = candidateProcess.StartTime.ToUniversalTime(),
            ArtifactRoot = candidateRoot,
            ArtifactId = "candidate",
            State = RuntimeReadinessState.Starting
        };
        var metadata = new RunnerRuntimeDeploymentMetadata
        {
            AppId = appId.ToString(),
            DeploymentTargetId = targetId.ToString(),
            Runtime = null,
            Health = null,
            State = RuntimeReadinessState.Stopped,
            Candidate = candidateRecord
        };
        var metadataPath = Path.Combine(workspace.GetDeploymentTargetRoot(appId, targetId), "deployment.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata));

        using var recoveryManager = CreateManager(workspace, registry);
        await recoveryManager.StartAsync(CancellationToken.None);

        var recovered = ReadMetadata(metadataPath);
        Assert.Multiple(() =>
        {
            Assert.That(IsProcessAlive(candidateProcess.Id), Is.False);
            Assert.That(recovered.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(recovered.Candidate, Is.Null);
            Assert.That(recovered.ArtifactRoot, Is.EqualTo(candidateRoot));
            Assert.That(recovered.ProcessId, Is.Not.EqualTo(candidateProcess.Id));
            Assert.That(IsProcessAlive(recovered.ProcessId!.Value), Is.True);
            Assert.That(Directory.Exists(candidateRoot), Is.True);
            Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
        });

        await recoveryManager.StopAsync(appId, targetId);
    }

    [Test]
    public void RunnerBoundary_RejectsAbsoluteAndTraversalPaths()
    {
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(new RunnerWorkspaceService(), registry);
        var cases = new[] { "../outside", "bin/../../outside", "/usr/bin/tool", "C:\\tools\\tool.exe" };

        foreach (var executable in cases)
        {
            var contract = CreateContract(["--tcp"]);
            contract.Runtime.Command!.Executable = executable;
            Assert.That(
                async () => await manager.ApplyAsync(Guid.NewGuid(), Guid.NewGuid(), contract),
                Throws.TypeOf<InvalidOperationException>(),
                executable);
        }
    }

    [Test]
    public void ArtifactRootOutsideManagedTarget_IsRejectedBeforeExecution()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(workspace, registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var outsideRoot = Path.Combine(root, "outside-artifacts");

        Assert.That(
            async () => await manager.ApplyAsync(
                appId,
                targetId,
                CreateContract(["--tcp"]),
                artifactRoot: outsideRoot),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(Directory.Exists(outsideRoot), Is.False);
    }

    [Test]
    public void StartupFailureIsDeterministic()
    {
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(new RunnerWorkspaceService(), registry);
        var contract = CreateContract(["--does-not-listen"]);
        contract.Runtime.Command!.Arguments = ["--version"];
        contract.Health.StartupTimeoutSeconds = 1;

        Assert.That(
            async () => await manager.ApplyAsync(Guid.NewGuid(), Guid.NewGuid(), contract),
            Throws.TypeOf<TimeoutException>());
    }

    [Test]
    public async Task Recovery_UsesGenericMetadataWithoutFrameworkFields()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contract = CreateContract(["--tcp"]);
        using (var firstManager = CreateManager(workspace, registry))
            await firstManager.ApplyAsync(appId, targetId, contract);

        using var recoveryManager = CreateManager(workspace, registry);
        await recoveryManager.StartAsync(CancellationToken.None);

        var metadataPath = Path.Combine(workspace.GetDeploymentTargetRoot(appId, targetId), "deployment.json");
        var metadataText = await File.ReadAllTextAsync(metadataPath);
        Assert.Multiple(() =>
        {
            Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
            Assert.That(metadataText, Does.Contain("effectivePort"));
            Assert.That(metadataText, Does.Contain("processId"));
            Assert.That(metadataText, Does.Not.Contain("hasBackend"));
            Assert.That(metadataText, Does.Not.Contain("backendModule"));
        });

        await recoveryManager.StopAsync(appId, targetId);
    }

    private static RunnerRuntimeProcessManager CreateManager(
        RunnerWorkspaceService workspace,
        RunnerRuntimeRegistry registry,
        Func<RunnerRuntimeDeploymentMetadata, Exception?>? metadataWriteFailureFactory = null)
        => new(
            workspace,
            registry,
            NullLogger<RunnerRuntimeProcessManager>.Instance,
            metadataWriteFailureFactory);

    private string CreateRuntimePackage(
        Guid appId,
        IReadOnlyList<string>? arguments = null,
        string marker = "deployed",
        ApplicationBuildSpec? build = null)
    {
        var packagePath = Path.Combine(root, $"{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(root);
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var contract = new ApplicationContract
        {
            Build = build ?? new ApplicationBuildSpec(),
            Runtime = new ApplicationRuntimeSpec
            {
                Command = new ApplicationCommandSpec
                {
                    Executable = "dotnet",
                    Arguments = arguments?.ToList() ?? ["RunnerRuntimeFixture.dll", "--tcp", "deployed"],
                    WorkingDirectory = "."
                },
                Port = 20_000 + Random.Shared.Next(1000)
            },
            Health = new ApplicationHealthSpec { Path = null, StartupTimeoutSeconds = 5 }
        };
        WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(new AppDeploymentPackageManifest
        {
            AppId = appId.ToString(),
            Application = contract
        }));

        var fixtureDirectory = Path.GetDirectoryName(FixturePath())!;
        foreach (var file in Directory.GetFiles(fixtureDirectory, "RunnerRuntimeFixture.*"))
            archive.CreateEntryFromFile(file, $"runtime/application/{Path.GetFileName(file)}");
        WriteEntry(archive, "runtime/application/marker.txt", marker);
        if (build?.Command is not null)
            WriteEntry(archive, "source/build-input.txt", "generic build input");
        return packagePath;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static RunnerRuntimeDeploymentMetadata ReadMetadata(string path)
        => JsonSerializer.Deserialize<RunnerRuntimeDeploymentMetadata>(
            File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static Process StartFixtureProcess(string workingDirectory, int port, string value)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Environment = { ["AUTOMATON_PORT"] = port.ToString(), ["TEST_RUNTIME_VALUE"] = value },
            ArgumentList = { FixturePath(), "--tcp", value }
        }) ?? throw new InvalidOperationException("Could not start runtime fixture.");
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        return process;
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task<bool> WaitForProcessExitAsync(int processId)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (!IsProcessAlive(processId))
                return true;
            await Task.Delay(100);
        }
        return !IsProcessAlive(processId);
    }

    private static async Task<string> WaitForLogLineAsync(string path, string marker)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (File.Exists(path))
            {
                var content = await ReadSharedTextAsync(path);
                if (content.Contains(marker, StringComparison.Ordinal))
                    return content;
            }
            await Task.Delay(100);
        }
        return await ReadSharedTextAsync(path);
    }

    private static ApplicationContract CreateContract(IReadOnlyList<string> arguments)
        => new()
        {
            Runtime = new ApplicationRuntimeSpec
            {
                Command = new ApplicationCommandSpec
                {
                    Executable = "dotnet",
                    Arguments = [FixturePath(), .. arguments],
                    WorkingDirectory = ".",
                    Environment = new Dictionary<string, string> { ["TEST_RUNTIME_VALUE"] = "expected" }
                },
                Port = 20_000 + Random.Shared.Next(1000)
            },
            Health = new ApplicationHealthSpec { Path = null, StartupTimeoutSeconds = 5 }
        };

    private static string FixturePath()
        => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "RunnerRuntimeFixture", "bin", "Debug", "net9.0", "RunnerRuntimeFixture.dll"));

    private static async Task<string> ReadSharedTextAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
