using System.Diagnostics;
using System.Runtime.InteropServices;
using Automaton.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.Runner.Console.Services;

public interface IAppRuntimeProcessManager
{
    Task<RuntimeApplyResult> ApplyAsync(
        Guid appId,
        Guid deploymentTargetId,
        ApplicationContract contract,
        CancellationToken cancellationToken = default,
        string? artifactRoot = null,
        Guid? candidateId = null);

    Task StopAsync(Guid appId, Guid deploymentTargetId);
}

public sealed class RuntimeApplyResult
{
    public bool PromotionIrreversible { get; init; }
    public string? Warning { get; init; }
}

/// <summary>
/// Starts and supervises the opaque runtime command described by an application
/// contract. This class intentionally has no knowledge of a language, framework,
/// package manager, or application source layout.
/// </summary>
public sealed class RunnerRuntimeProcessManager : IAppRuntimeProcessManager, IHostedService, IDisposable
{
    private const string RuntimePortEnvironmentVariable = "AUTOMATON_PORT";
    private readonly RunnerWorkspaceService workspace;
    private readonly RunnerRuntimeProcessMetadataStore metadataStore;
    private readonly RunnerRuntimeProcessPortAllocator portAllocator = new();
    private readonly RunnerRuntimeRegistry registry;
    private readonly ILogger<RunnerRuntimeProcessManager> logger;
    private readonly object gate = new();
    private readonly Dictionary<DeploymentRuntimeKey, ActiveRuntime> runtimes = [];
    private readonly Dictionary<DeploymentRuntimeKey, ActiveRuntime> candidates = [];

    public RunnerRuntimeProcessManager(
        RunnerWorkspaceService workspace,
        RunnerRuntimeRegistry registry,
        ILogger<RunnerRuntimeProcessManager> logger,
        Func<RunnerRuntimeDeploymentMetadata, Exception?>? metadataWriteFailureFactory = null)
    {
        this.workspace = workspace;
        metadataStore = new RunnerRuntimeProcessMetadataStore(workspace, metadataWriteFailureFactory);
        this.registry = registry;
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var metadataPath in Directory.Exists(workspace.AppsRoot)
            ? Directory.EnumerateFiles(workspace.AppsRoot, "deployment.json", SearchOption.AllDirectories)
            : [])
        {
            try
            {
                var metadata = await metadataStore.ReadAsync(metadataPath, cancellationToken);
                if (metadata is null || (metadata.Runtime?.Command is null && metadata.Candidate?.Runtime?.Command is null))
                    continue;

                if (!Guid.TryParse(metadata.AppId, out var appId)
                    || !Guid.TryParse(metadata.DeploymentTargetId, out var targetId))
                {
                    throw new InvalidOperationException("Runtime deployment metadata contains invalid identifiers.");
                }

                await StopRecordedProcessAsync(metadata);
                if (metadata.Candidate is not null)
                    await StopRecordedProcessAsync(metadata.Candidate);

                var candidate = metadata.Candidate ?? CreateRecoveryCandidate(metadata);
                var deleteCandidateArtifactOnFailure = metadata.Candidate is not null;
                metadata.Candidate = candidate;
                candidate.ProcessId = null;
                candidate.ProcessStartTimeUtc = null;
                candidate.EffectivePort = null;
                candidate.State = RuntimeReadinessState.Starting;
                await metadataStore.WriteAsync(metadata, cancellationToken);

                var activeCandidate = await StartRuntimeAsync(
                    appId,
                    targetId,
                    candidate,
                    metadata,
                    cancellationToken,
                    deleteCandidateArtifactOnFailure);
                await PromoteRuntimeAsync(
                    appId,
                    targetId,
                    metadata,
                    activeCandidate,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not restore runtime process from {MetadataPath}", metadataPath);
            }
        }
    }

    public async Task<RuntimeApplyResult> ApplyAsync(
        Guid appId,
        Guid deploymentTargetId,
        ApplicationContract contract,
        CancellationToken cancellationToken = default,
        string? artifactRoot = null,
        Guid? candidateId = null)
    {
        ApplicationContractValidator.EnsureValid(contract);
        var metadataPath = metadataStore.GetPath(appId, deploymentTargetId);
        var previousMetadata = File.Exists(metadataPath)
            ? await metadataStore.ReadAsync(metadataPath, cancellationToken)
            : null;

        if (previousMetadata?.Candidate is not null)
        {
            await StopRecordedProcessAsync(previousMetadata.Candidate);
            DeleteArtifactDirectory(previousMetadata.Candidate.ArtifactRoot, previousMetadata.ArtifactRoot);
            previousMetadata.Candidate = null;
        }

        if (contract.Runtime.Command is null)
        {
            await StopAsync(appId, deploymentTargetId);
            await metadataStore.WriteAsync(
                new RunnerRuntimeDeploymentMetadata
                {
                    AppId = appId.ToString(),
                    DeploymentTargetId = deploymentTargetId.ToString(),
                    Runtime = contract.Runtime,
                    Health = contract.Health,
                    Storage = contract.Storage,
                    State = RuntimeReadinessState.Stopped
                },
                cancellationToken);
            return new RuntimeApplyResult();
        }

        var candidate = new RunnerRuntimeDeploymentMetadata
        {
            AppId = appId.ToString(),
            DeploymentTargetId = deploymentTargetId.ToString(),
            Runtime = contract.Runtime,
            Health = contract.Health,
            Storage = contract.Storage,
            ArtifactRoot = artifactRoot ?? workspace.GetAppRuntimeCandidateApplicationDirectory(
                appId,
                deploymentTargetId,
                Guid.NewGuid()),
            ArtifactId = null,
            State = RuntimeReadinessState.Starting
        };
        candidate.ArtifactRoot = ValidateArtifactRoot(
            appId,
            deploymentTargetId,
            candidate.ArtifactRoot);
        candidate.ArtifactId = GetArtifactId(candidate.ArtifactRoot);
        Directory.CreateDirectory(candidate.ArtifactRoot);
        var pending = CreatePendingMetadata(appId, deploymentTargetId, previousMetadata, candidate);
        await metadataStore.WriteAsync(pending, cancellationToken);

        ActiveRuntime? activeCandidate = null;
        try
        {
            activeCandidate = await StartRuntimeAsync(
                appId,
                deploymentTargetId,
                candidate,
                pending,
                cancellationToken,
                deleteArtifactOnFailure: true);
            return await PromoteRuntimeAsync(
                appId,
                deploymentTargetId,
                pending,
                activeCandidate,
                cancellationToken);
        }
        catch
        {
            if (activeCandidate is null || !IsCurrentRuntime(
                    new DeploymentRuntimeKey(appId, deploymentTargetId),
                    activeCandidate))
            {
                if (previousMetadata is not null)
                    await metadataStore.WriteAsync(previousMetadata, CancellationToken.None);
                else if (File.Exists(metadataPath))
                    File.Delete(metadataPath);
            }
            throw;
        }
    }

    private async Task<ActiveRuntime> StartRuntimeAsync(
        Guid appId,
        Guid deploymentTargetId,
        RunnerRuntimeDeploymentMetadata candidate,
        RunnerRuntimeDeploymentMetadata metadata,
        CancellationToken cancellationToken,
        bool deleteArtifactOnFailure)
    {
        var command = candidate.Runtime?.Command
            ?? throw new InvalidOperationException("A runtime command is required to start a runtime process.");
        var key = new DeploymentRuntimeKey(appId, deploymentTargetId);
        var requestedPort = candidate.EffectivePort ?? candidate.Runtime.Port;
        var effectivePort = AllocatePort(requestedPort);
        var targetRoot = ValidateArtifactRoot(appId, deploymentTargetId, candidate.ArtifactRoot);
        candidate.ArtifactRoot = targetRoot;
        Directory.CreateDirectory(targetRoot);
        var workingDirectory = RunnerRuntimeProcessExecutionPolicy.ResolveWorkspacePath(
            targetRoot, command.WorkingDirectory, "working directory");
        var executable = RunnerRuntimeProcessExecutionPolicy.ResolveExecutable(targetRoot, command.Executable);
        var storage = workspace.PrepareApplicationStorage(
            appId,
            candidate.Storage ?? new ApplicationStorageSpec());
        var logDirectory = workspace.GetAppRuntimeLogsDirectory(
            appId,
            deploymentTargetId,
            candidate.ArtifactId ?? Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDirectory);
        var stdout = RunnerRuntimeProcessExecutionPolicy.CreateLogWriter(Path.Combine(logDirectory, "runtime.stdout.log"));
        var stderr = RunnerRuntimeProcessExecutionPolicy.CreateLogWriter(Path.Combine(logDirectory, "runtime.stderr.log"));
        Process? process = null;
        ActiveRuntime? active = null;

        try
        {
            var startInfo = RunnerRuntimeProcessExecutionPolicy.CreateStartInfo(
                executable,
                command,
                workingDirectory,
                effectivePort,
                appId,
                deploymentTargetId,
                targetRoot,
                storage);
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Could not start runtime process for app {appId}.");
            process.OutputDataReceived += (_, args) => RunnerRuntimeProcessExecutionPolicy.WriteLine(stdout, args.Data);
            process.ErrorDataReceived += (_, args) => RunnerRuntimeProcessExecutionPolicy.WriteLine(stderr, args.Data);
            process.EnableRaisingEvents = true;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var runtimeProcess = new ActiveRuntime(
                process,
                effectivePort,
                stdout,
                stderr,
                targetRoot);
            active = runtimeProcess;
            lock (gate) candidates[key] = runtimeProcess;
            process.Exited += (_, _) => OnProcessExited(key, runtimeProcess);

            candidate.EffectivePort = effectivePort;
            candidate.ProcessId = process.Id;
            candidate.ProcessStartTimeUtc = RunnerRuntimeProcessExecutionPolicy.GetProcessStartTime(process);
            candidate.StartedAtUtc = DateTimeOffset.UtcNow;
            candidate.State = RuntimeReadinessState.Starting;
            metadata.Candidate = candidate;
            await metadataStore.WriteAsync(metadata, cancellationToken);

            await RunnerRuntimeProcessExecutionPolicy.WaitForReadinessAsync(
                runtimeProcess.Process,
                effectivePort,
                candidate.Health ?? new ApplicationHealthSpec(),
                cancellationToken);
            if (runtimeProcess.HasExited)
                throw new InvalidOperationException("Runtime process exited immediately after becoming ready.");
            return runtimeProcess;
        }
        catch
        {
            if (process is not null)
            {
                await RunnerRuntimeProcessExecutionPolicy.StopProcessAsync(process, cancellationToken);
                RemoveActiveRuntime(key, process);
            }
            if (active is not null)
            {
                active.Dispose();
            }
            else
            {
                stdout.Dispose();
                stderr.Dispose();
            }
            if (deleteArtifactOnFailure)
                DeleteArtifactDirectory(candidate.ArtifactRoot);
            throw;
        }
    }

    private async Task<RuntimeApplyResult> PromoteRuntimeAsync(
        Guid appId,
        Guid deploymentTargetId,
        RunnerRuntimeDeploymentMetadata metadata,
        ActiveRuntime candidate,
        CancellationToken cancellationToken)
    {
        var key = new DeploymentRuntimeKey(appId, deploymentTargetId);
        var promotionIrreversible = false;
        try
        {
            if (candidate.HasExited)
                throw new InvalidOperationException(
                    $"Runtime process exited before becoming ready (exit code {candidate.ExitCode?.ToString() ?? "unknown"}).");

            var candidateMetadata = metadata.Candidate
                ?? throw new InvalidOperationException("Runtime candidate metadata is missing.");
            candidateMetadata.EffectivePort = candidate.Port;
            candidateMetadata.ProcessId = candidate.Process.Id;
            candidateMetadata.ProcessStartTimeUtc = RunnerRuntimeProcessExecutionPolicy.GetProcessStartTime(candidate.Process);
            candidateMetadata.State = RuntimeReadinessState.Ready;
            await metadataStore.WriteAsync(metadata, cancellationToken);

            ActiveRuntime? old;
            lock (gate)
            {
                if (candidate.HasExited)
                    throw new InvalidOperationException(
                        $"Runtime process exited before promotion (exit code {candidate.ExitCode?.ToString() ?? "unknown"}).");

                old = runtimes.GetValueOrDefault(key);
                runtimes[key] = candidate;
                candidates.Remove(key);
                registry.Register(appId, deploymentTargetId, candidate.Port);
            }
            promotionIrreversible = true;

            if (old is not null && !old.HasExited)
            await RunnerRuntimeProcessExecutionPolicy.StopProcessAsync(old.Process, cancellationToken);

            if (old is not null)
                DeleteArtifactDirectory(old.ArtifactRoot, candidate.ArtifactRoot);

            metadata.Runtime = candidateMetadata.Runtime;
            metadata.Health = candidateMetadata.Health;
            metadata.Storage = candidateMetadata.Storage;
            metadata.EffectivePort = candidateMetadata.EffectivePort;
            metadata.ProcessId = candidateMetadata.ProcessId;
            metadata.ProcessStartTimeUtc = candidateMetadata.ProcessStartTimeUtc;
            metadata.StartedAtUtc = candidateMetadata.StartedAtUtc;
            metadata.ArtifactRoot = candidateMetadata.ArtifactRoot;
            metadata.ArtifactId = candidateMetadata.ArtifactId;
            metadata.State = RuntimeReadinessState.Ready;
            metadata.LastError = null;
            metadata.Candidate = null;
            try
            {
                await metadataStore.WriteAsync(metadata, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Runtime promotion completed but metadata compaction failed for app {AppId}, target {DeploymentTargetId}",
                    appId,
                    deploymentTargetId);
                return new RuntimeApplyResult
                {
                    PromotionIrreversible = true,
                    Warning = "Runtime started successfully, but final deployment metadata compaction will be reconciled on runner restart."
                };
            }

            return new RuntimeApplyResult { PromotionIrreversible = true };
        }
        catch (Exception ex) when (promotionIrreversible)
        {
            logger.LogWarning(
                ex,
                "Runtime promotion crossed its irreversible boundary for app {AppId}, target {DeploymentTargetId}; recovery will reconcile the durable candidate state",
                appId,
                deploymentTargetId);
            return new RuntimeApplyResult
            {
                PromotionIrreversible = true,
                Warning = "Runtime promotion crossed its irreversible boundary and will be reconciled from durable metadata."
            };
        }
        catch
        {
            if (!IsCurrentRuntime(key, candidate))
            {
                await RunnerRuntimeProcessExecutionPolicy.StopProcessAsync(candidate.Process, cancellationToken);
                RemoveActiveRuntime(key, candidate.Process);
                candidate.Dispose();
            }
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ActiveRuntime[] active;
        DeploymentRuntimeKey[] keys;
        lock (gate)
        {
            active = runtimes.Values.ToArray();
            keys = runtimes.Keys.ToArray();
            active = active.Concat(candidates.Values).ToArray();
            runtimes.Clear();
            candidates.Clear();
        }

        foreach (var key in keys)
            registry.Remove(key.AppId, key.DeploymentTargetId);
        foreach (var runtime in active)
        {
            await RunnerRuntimeProcessExecutionPolicy.StopProcessAsync(runtime.Process, cancellationToken);
            runtime.Dispose();
        }
    }

    public async Task StopAsync(Guid appId, Guid deploymentTargetId)
    {
        var key = new DeploymentRuntimeKey(appId, deploymentTargetId);
        registry.Remove(appId, deploymentTargetId);
        ActiveRuntime[] active;
        lock (gate)
        {
            var selected = new List<ActiveRuntime>();
            if (runtimes.Remove(key, out var current))
                selected.Add(current);
            if (candidates.Remove(key, out var candidate)
                && !selected.Contains(candidate))
            {
                selected.Add(candidate);
            }

            active = selected.ToArray();
        }

        foreach (var runtime in active)
        {
            await RunnerRuntimeProcessExecutionPolicy.StopProcessAsync(runtime.Process);
            runtime.Dispose();
        }
    }

    private void OnProcessExited(DeploymentRuntimeKey key, ActiveRuntime active)
    {
        active.MarkExited();
        var isCurrent = false;
        var isCandidate = false;
        lock (gate)
        {
            isCurrent = runtimes.TryGetValue(key, out var current) && ReferenceEquals(current, active);
            if (isCurrent) runtimes.Remove(key);
            isCandidate = candidates.TryGetValue(key, out var candidate) && ReferenceEquals(candidate, active);
            if (isCandidate)
                candidates.Remove(key);
        }

        if (isCurrent)
        {
            registry.Remove(key.AppId, key.DeploymentTargetId);
            logger.LogWarning(
                "Runtime process exited unexpectedly for app {AppId}, target {DeploymentTargetId}",
                key.AppId,
                key.DeploymentTargetId);
            _ = MarkStoppedAsync(key, active.ExitCode ?? -1);
        }

        if (!isCandidate)
            active.Dispose();
    }

    private async Task MarkStoppedAsync(DeploymentRuntimeKey key, int exitCode)
    {
        try
        {
            var path = metadataStore.GetPath(key.AppId, key.DeploymentTargetId);
            var metadata = await metadataStore.ReadAsync(path, CancellationToken.None);
            if (metadata is not null)
            {
                metadata.EffectivePort = null;
                metadata.ProcessId = null;
                metadata.ProcessStartTimeUtc = null;
                metadata.State = RuntimeReadinessState.Failed;
                metadata.LastError = $"Runtime process exited with code {exitCode}.";
                await metadataStore.WriteAsync(metadata, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not persist runtime exit state for app {AppId}", key.AppId);
        }
    }

    private async Task StopRecordedProcessAsync(RunnerRuntimeDeploymentMetadata metadata)
    {
        if (metadata.ProcessId is not int processId || processId <= 0)
            return;

        try
        {
            using var process = Process.GetProcessById(processId);
            if (metadata.ProcessStartTimeUtc is DateTimeOffset expected)
            {
                var actual = process.StartTime.ToUniversalTime();
                if (Math.Abs((actual - expected.UtcDateTime).TotalSeconds) > 2)
                    return;
            }

            await RunnerRuntimeProcessExecutionPolicy.StopProcessAsync(process);
        }
        catch (ArgumentException)
        {
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static RunnerRuntimeDeploymentMetadata CreatePendingMetadata(
        Guid appId,
        Guid deploymentTargetId,
        RunnerRuntimeDeploymentMetadata? previous,
        RunnerRuntimeDeploymentMetadata candidate)
        => new()
        {
            AppId = appId.ToString(),
            DeploymentTargetId = deploymentTargetId.ToString(),
            Runtime = previous?.Runtime,
            Health = previous?.Health,
            Storage = previous?.Storage,
            EffectivePort = previous?.EffectivePort,
            ProcessId = previous?.ProcessId,
            ProcessStartTimeUtc = previous?.ProcessStartTimeUtc,
            StartedAtUtc = previous?.StartedAtUtc,
            ArtifactRoot = previous?.ArtifactRoot,
            ArtifactId = previous?.ArtifactId,
            State = previous?.State ?? RuntimeReadinessState.Stopped,
            LastError = null,
            Candidate = candidate
        };

    private static RunnerRuntimeDeploymentMetadata CreateRecoveryCandidate(
        RunnerRuntimeDeploymentMetadata current)
        => new()
        {
            AppId = current.AppId,
            DeploymentTargetId = current.DeploymentTargetId,
            Runtime = current.Runtime,
            Health = current.Health,
            Storage = current.Storage,
            ArtifactRoot = current.ArtifactRoot,
            ArtifactId = current.ArtifactId,
            State = RuntimeReadinessState.Starting
        };

    private static string? GetArtifactId(string? artifactRoot)
    {
        if (string.IsNullOrWhiteSpace(artifactRoot))
            return null;

        var applicationDirectory = Directory.GetParent(artifactRoot);
        return applicationDirectory is not null
            && string.Equals(applicationDirectory.Name, "application", StringComparison.OrdinalIgnoreCase)
            ? applicationDirectory.Parent?.Name
            : applicationDirectory?.Name;
    }

    private string ValidateArtifactRoot(Guid appId, Guid deploymentTargetId, string? artifactRoot)
    {
        var fullArtifactRoot = Path.GetFullPath(
            artifactRoot ?? workspace.GetAppRuntimeApplicationDirectory(appId, deploymentTargetId));
        var canonicalRoot = Path.GetFullPath(
            workspace.GetAppRuntimeApplicationDirectory(appId, deploymentTargetId));
        if (string.Equals(fullArtifactRoot, canonicalRoot, StringComparison.OrdinalIgnoreCase))
            return fullArtifactRoot;

        var artifactsRoot = Path.GetFullPath(workspace.GetAppArtifactsRoot(appId, deploymentTargetId));
        var candidateDirectory = Directory.GetParent(fullArtifactRoot);
        var candidatesDirectory = candidateDirectory is null
            ? null
            : Directory.GetParent(candidateDirectory.FullName);
        var candidateContainerParent = candidatesDirectory is null
            ? null
            : Directory.GetParent(candidatesDirectory.FullName);
        if (string.Equals(Path.GetFileName(fullArtifactRoot), "application", StringComparison.OrdinalIgnoreCase)
            && candidateDirectory is not null
            && candidatesDirectory is not null
            && candidateContainerParent is not null
            && string.Equals(Path.GetFileName(candidatesDirectory.FullName), "candidates", StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidateContainerParent.FullName, artifactsRoot, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(Path.GetFileName(candidateDirectory.FullName))
            && Path.GetFileName(candidateDirectory.FullName) is not "." and not "..")
            return fullArtifactRoot;

        throw new InvalidOperationException(
            "Runtime artifact root must be the managed application runtime directory or a managed candidate artifact directory.");
    }

    private void DeleteArtifactDirectory(string? artifactRoot, string? preserveRoot = null)
    {
        if (string.IsNullOrWhiteSpace(artifactRoot)
            || string.Equals(artifactRoot, preserveRoot, StringComparison.OrdinalIgnoreCase))
            return;

        var fullArtifactRoot = Path.GetFullPath(artifactRoot);
        var appsRoot = Path.GetFullPath(workspace.AppsRoot);
        if (!ManagedPathBoundary.IsWithin(appsRoot, fullArtifactRoot))
            throw new InvalidOperationException("Runtime artifact root is outside the runner workspace.");
        if (!string.Equals(Path.GetFileName(fullArtifactRoot), "application", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Runtime artifact root is not a managed application directory.");

        var container = Directory.GetParent(fullArtifactRoot)?.FullName;
        var containerParent = container is null
            ? null
            : Directory.GetParent(container)?.FullName;
        if (containerParent is not null
            && string.Equals(Path.GetFileName(containerParent), "candidates", StringComparison.OrdinalIgnoreCase))
            DeleteDirectoryIfExists(container!);
        else if (container is not null
            && string.Equals(Path.GetFileName(container), "current", StringComparison.OrdinalIgnoreCase)
            && containerParent is not null
            && string.Equals(Path.GetFileName(containerParent), "artifacts", StringComparison.OrdinalIgnoreCase))
            DeleteDirectoryIfExists(container);
        else
            throw new InvalidOperationException("Runtime artifact root is not a managed application directory.");
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private bool IsCurrentRuntime(DeploymentRuntimeKey key, ActiveRuntime runtime)
    {
        lock (gate)
            return runtimes.TryGetValue(key, out var current) && ReferenceEquals(current, runtime);
    }

    private void RemoveActiveRuntime(DeploymentRuntimeKey key, Process process)
    {
        lock (gate)
        {
            if (runtimes.TryGetValue(key, out var current) && ReferenceEquals(current.Process, process))
                runtimes.Remove(key);
            if (candidates.TryGetValue(key, out var candidate) && ReferenceEquals(candidate.Process, process))
                candidates.Remove(key);
        }
    }

    private int AllocatePort(int requestedPort)
    {
        lock (gate)
        {
            var used = runtimes.Values.Concat(candidates.Values)
                .Where(runtime => !runtime.HasExited)
                .Select(runtime => runtime.Port)
                .ToHashSet();
            return portAllocator.AllocatePort(requestedPort, used);
        }
    }

    public void Dispose() => StopAsync(CancellationToken.None).GetAwaiter().GetResult();

    private sealed class ActiveRuntime(
        Process process,
        int port,
        StreamWriter stdout,
        StreamWriter stderr,
        string artifactRoot) : IDisposable
    {
        private int disposed;
        private int exited;
        private int exitCode = int.MinValue;
        public Process Process { get; } = process;
        public int Port { get; } = port;
        public string ArtifactRoot { get; } = artifactRoot;
        public bool HasExited
        {
            get
            {
                if (Volatile.Read(ref exited) != 0)
                    return true;

                try { return Process.HasExited; }
                catch (InvalidOperationException) { return true; }
            }
        }
        public int? ExitCode
        {
            get
            {
                var value = Volatile.Read(ref exitCode);
                return value == int.MinValue ? null : value;
            }
        }

        public void MarkExited()
        {
            try { Interlocked.Exchange(ref exitCode, Process.ExitCode); }
            catch (InvalidOperationException) { }
            Volatile.Write(ref exited, 1);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            stdout.Dispose();
            stderr.Dispose();
            Process.Dispose();
        }
    }
}

public static class RuntimeReadinessState
{
    public const string Starting = "starting";
    public const string Ready = "ready";
    public const string Stopped = "stopped";
    public const string Failed = "failed";
}

public sealed class RunnerRuntimeDeploymentMetadata
{
    public string AppId { get; set; } = string.Empty;
    public string DeploymentTargetId { get; set; } = string.Empty;
    public ApplicationRuntimeSpec? Runtime { get; set; }
    public ApplicationHealthSpec? Health { get; set; }
    public ApplicationStorageSpec? Storage { get; set; }
    public int? EffectivePort { get; set; }
    public int? ProcessId { get; set; }
    public DateTimeOffset? ProcessStartTimeUtc { get; set; }
    public string? ContainerId { get; set; }
    public string? ContainerName { get; set; }
    public string? RequestedRuntimeImage { get; set; }
    public string? ResolvedRuntimeImage { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public string? ArtifactRoot { get; set; }
    public string? ArtifactId { get; set; }
    public Guid? CandidateId { get; set; }
    public string State { get; set; } = RuntimeReadinessState.Stopped;
    public string? LastError { get; set; }
    public RunnerRuntimeDeploymentMetadata? Candidate { get; set; }
}
