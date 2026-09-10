using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Automaton.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Supervises managed application containers. The Runner owns lifecycle and
/// readiness; the application image owns the process and its HTTP behavior.
/// </summary>
public sealed class RunnerRuntimeContainerManager : IAppRuntimeProcessManager, IHostedService
{
    private const string MetadataFileName = "deployment.json";
    private readonly RunnerWorkspaceService workspace;
    private readonly RunnerRuntimeRegistry registry;
    private readonly IApplicationContainerRuntime containers;
    private readonly ILogger<RunnerRuntimeContainerManager> logger;
    private readonly object gate = new();
    private readonly Dictionary<DeploymentRuntimeKey, ActiveContainer> active = [];
    private readonly HashSet<string> pendingReconciliation = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource supervisionCts = new();
    private volatile bool acceptingLifecycleWork;
    private Task? supervisionTask;
    private readonly int portStart = ReadPort("AUTOMATON_RUNTIME_PORT_START", 18000);
    private readonly int portEnd = ReadPort("AUTOMATON_RUNTIME_PORT_END", 18999);
    private readonly string runtimeHostAddress = RunnerRuntimeEndpointConfiguration.HostAddress;
    private readonly bool canProbeRuntimeHost;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public RunnerRuntimeContainerManager(
        RunnerWorkspaceService workspace,
        RunnerRuntimeRegistry registry,
        IApplicationContainerRuntime containers,
        ILogger<RunnerRuntimeContainerManager> logger)
    {
        this.workspace = workspace;
        this.registry = registry;
        this.containers = containers;
        this.logger = logger;
        canProbeRuntimeHost = IPAddress.IsLoopback(IPAddress.Parse(runtimeHostAddress));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        acceptingLifecycleWork = true;
        foreach (var metadataPath in Directory.Exists(workspace.AppsRoot)
            ? Directory.EnumerateFiles(workspace.AppsRoot, MetadataFileName, SearchOption.AllDirectories)
            : [])
        {
            try
            {
                await ReconcileMetadataAsync(metadataPath, cancellationToken);
            }
            catch (Exception ex)
            {
                lock (gate) pendingReconciliation.Add(metadataPath);
                logger.LogWarning(ex, "Could not reconcile managed application container metadata from {MetadataPath}", metadataPath);
            }
        }
        supervisionTask = Task.Run(() => SuperviseAsync(supervisionCts.Token), CancellationToken.None);
    }

    private async Task ReconcileMetadataAsync(string metadataPath, CancellationToken cancellationToken)
    {
        var metadata = await ReadMetadataAsync(metadataPath, cancellationToken);
        if (metadata is null)
            return;
        if (!Guid.TryParse(metadata.AppId, out var appId)
            || !Guid.TryParse(metadata.DeploymentTargetId, out var targetId))
            throw new InvalidOperationException("Runtime deployment metadata contains invalid identifiers.");

        var candidateIsOci = IsOciCandidateMetadata(metadata.Candidate);
        var currentIsOci = IsOciContainerMetadata(metadata);
        if (!candidateIsOci && !currentIsOci)
            return;

        var candidate = metadata.Candidate;
        if (candidateIsOci && candidate is not null)
        {
            var candidateIdentity = await InspectAsync(appId, targetId, candidate.ContainerId ?? candidate.ContainerName!, cancellationToken);
            if (candidateIdentity.Exists
                && candidateIdentity.Running
                && HasExpectedIdentity(candidateIdentity, appId, targetId)
                && string.Equals(candidate.State, RuntimeReadinessState.Ready, StringComparison.Ordinal))
            {
                if (candidateIdentity.HostPort is > 0)
                    candidate.EffectivePort = candidateIdentity.HostPort;
                if (currentIsOci
                    && !string.IsNullOrWhiteSpace(metadata.ContainerId)
                    && !string.Equals(metadata.ContainerId, candidate.ContainerId, StringComparison.Ordinal))
                {
                    var previousIdentity = await InspectAsync(appId, targetId, metadata.ContainerId, cancellationToken);
                    if (previousIdentity.Exists && HasExpectedIdentity(previousIdentity, appId, targetId))
                        await StopAndRemoveAsync(appId, targetId, previousIdentity.Id, CancellationToken.None);
                    else
                        logger.LogWarning("Preserving previous runtime container {ContainerId} because its Automaton ownership labels could not be verified.", metadata.ContainerId);
                }
                var promoted = CloneMetadata(candidate);
                promoted.Candidate = null;
                promoted.State = RuntimeReadinessState.Ready;
                await WriteMetadataAsync(promoted, cancellationToken);
                Register(appId, targetId, candidateIdentity.Id, promoted.ContainerName, promoted.EffectivePort);
                return;
            }

            if (candidateIdentity.Exists && HasExpectedIdentity(candidateIdentity, appId, targetId))
            {
                logger.LogWarning("Discarding an uncommitted managed runtime candidate {ContainerId}.", candidate.ContainerId);
                try
                {
                    await StopAndRemoveAsync(appId, targetId, candidateIdentity.Id, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not remove invalid managed runtime candidate {ContainerId}; retaining metadata for retry.", candidateIdentity.Id);
                    throw;
                }
                metadata.Candidate = null;
                await WriteMetadataAsync(metadata, cancellationToken);
            }
            else if (candidateIdentity.Exists)
            {
                throw new InvalidOperationException($"Refusing to reconcile candidate container '{candidate.ContainerId}' without verified Automaton ownership labels.");
            }
            else
            {
                metadata.Candidate = null;
                await WriteMetadataAsync(metadata, cancellationToken);
            }
        }

        if (!currentIsOci || string.IsNullOrWhiteSpace(metadata.ContainerId))
            return;

        var recorded = metadata.ContainerId;
        var identity = await InspectAsync(appId, targetId, recorded, cancellationToken);
        if (identity.Exists
            && identity.Running
            && HasExpectedIdentity(identity, appId, targetId))
        {
            if (identity.HostPort is > 0)
                metadata.EffectivePort = identity.HostPort;
            await WriteMetadataAsync(metadata, cancellationToken);
            Register(appId, targetId, identity.Id, metadata.ContainerName, metadata.EffectivePort);
            return;
        }
        logger.LogWarning("Refusing to adopt container {ContainerId}: it is missing, stopped, or does not have the expected Automaton identity.", recorded);

        if (string.IsNullOrWhiteSpace(metadata.ArtifactRoot) || !Directory.Exists(metadata.ArtifactRoot))
            return;
        await ApplyAsync(
            appId,
            targetId,
            new ApplicationContract
            {
                Runtime = CloneRuntimeWithResolvedImage(metadata.Runtime, metadata.ResolvedRuntimeImage),
                Health = metadata.Health ?? new ApplicationHealthSpec(),
                Storage = metadata.Storage ?? new ApplicationStorageSpec()
            },
            cancellationToken,
            metadata.ArtifactRoot,
            metadata.CandidateId);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        acceptingLifecycleWork = false;
        supervisionCts.Cancel();
        if (supervisionTask is not null)
            await supervisionTask.WaitAsync(cancellationToken);
        lock (gate)
        {
            foreach (var runtime in active.Values)
                registry.Remove(runtime.AppId, runtime.TargetId);
            active.Clear();
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
        if (!acceptingLifecycleWork)
            throw new InvalidOperationException("The managed runtime container manager is stopping and is not accepting new lifecycle work.");
        ApplicationContractValidator.EnsureDeployable(contract);
        if (contract.Health.Port is not null && contract.Health.Port != contract.Runtime.Port)
            throw new InvalidOperationException("A health.port different from runtime.port is not supported by the isolated application-container network boundary.");
        var metadataPath = GetMetadataPath(appId, deploymentTargetId);
        var previous = File.Exists(metadataPath)
            ? await ReadMetadataAsync(metadataPath, cancellationToken)
            : null;

        if (contract.Runtime.Command is null)
        {
            await StopAsync(appId, deploymentTargetId);
            await WriteMetadataAsync(new RunnerRuntimeDeploymentMetadata
            {
                AppId = appId.ToString(),
                DeploymentTargetId = deploymentTargetId.ToString(),
                Runtime = contract.Runtime,
                Health = contract.Health,
                Storage = contract.Storage,
                State = RuntimeReadinessState.Stopped
            }, cancellationToken);
            return new RuntimeApplyResult();
        }

        var runtimeImage = contract.Runtime.Image!;
        var containerPort = contract.Runtime.Port;
        var scopedRuntime = containers as IScopedApplicationContainerRuntime;
        var effectivePort = scopedRuntime is null ? AllocatePort(containerPort) : 0;
        var resolvedCandidateId = candidateId ?? Guid.NewGuid();
        var artifact = artifactRoot ?? workspace.GetAppRuntimeCandidateApplicationDirectory(
            appId, deploymentTargetId, resolvedCandidateId);
        artifact = ValidateArtifactRoot(appId, deploymentTargetId, artifact);
        Directory.CreateDirectory(artifact);
        var storage = workspace.PrepareApplicationStorage(appId, contract.Storage);
        var containerName = $"automaton-app-{appId:N}-{deploymentTargetId:N}-{Guid.NewGuid():N}";
        var candidate = new RunnerRuntimeDeploymentMetadata
        {
            AppId = appId.ToString(),
            DeploymentTargetId = deploymentTargetId.ToString(),
            Runtime = contract.Runtime,
            Health = contract.Health,
            Storage = contract.Storage,
            ArtifactRoot = artifact,
            ArtifactId = Path.GetFileName(Path.GetDirectoryName(artifact) ?? artifact),
            CandidateId = resolvedCandidateId,
            ContainerName = containerName,
            RequestedRuntimeImage = runtimeImage,
            EffectivePort = effectivePort,
            State = RuntimeReadinessState.Starting
        };
        var pending = previous is null
            ? new RunnerRuntimeDeploymentMetadata
            {
                AppId = appId.ToString(),
                DeploymentTargetId = deploymentTargetId.ToString(),
                Runtime = contract.Runtime,
                Health = contract.Health,
                Storage = contract.Storage,
                State = RuntimeReadinessState.Starting
            }
            : CloneMetadata(previous);
        pending.Candidate = candidate;
        pending.State = RuntimeReadinessState.Starting;
        await WriteMetadataAsync(pending, cancellationToken);

        DockerApplicationContainer? started = null;
        try
        {
            if (containers is not IStagedApplicationContainerRuntime stagedContainers)
                throw new InvalidOperationException("Managed application lifecycle requires the staged Docker container boundary.");
            started = await stagedContainers.CreateApplicationAsync(
                new DockerApplicationContainerRequest(
                    containerName,
                    runtimeImage,
                    artifact,
                    contract.Runtime.Command.WorkingDirectory,
                    contract.Runtime.Command,
                    containerPort,
                    effectivePort,
                    storage.DataDirectory,
                    storage.Mounts,
                    appId,
                    deploymentTargetId,
                    resolvedCandidateId,
                    contract.Storage),
                cancellationToken);
            candidate.ContainerId = started.Id;
            candidate.ContainerName = started.Name;
            candidate.RequestedRuntimeImage = started.Image.RequestedReference;
            candidate.ResolvedRuntimeImage = started.Image.ResolvedReference;
            await WriteMetadataAsync(pending, cancellationToken);

            if (scopedRuntime is not null)
            {
                var endpoint = await scopedRuntime.StartApplicationAsync(new(appId, deploymentTargetId), started.Id, cancellationToken);
                effectivePort = endpoint.HostPort;
                candidate.EffectivePort = effectivePort;
                await WriteMetadataAsync(pending, cancellationToken);
            }
            else
                await stagedContainers.StartApplicationAsync(started.Id, cancellationToken);
            await WaitForReadinessAsync(effectivePort, contract.Health, cancellationToken);
            if (!await IsRunningAsync(appId, deploymentTargetId, started.Id, cancellationToken))
                throw new InvalidOperationException("The runtime container exited after readiness and before promotion.");
            var warning = await PromoteAsync(appId, deploymentTargetId, previous, candidate, cancellationToken);
            return new RuntimeApplyResult { PromotionIrreversible = true, Warning = warning };
        }
        catch (Exception deploymentError)
        {
            if (started is not null)
            {
                try
                {
                    var logs = await GetLogsAsync(appId, deploymentTargetId, started.Id, CancellationToken.None);
                    logger.LogWarning(
                        "Managed application container {ContainerId} failed before promotion. Logs: {Logs}",
                        started.Id,
                        logs);
                }
                catch (Exception cleanupError)
                {
                    logger.LogWarning(cleanupError, "Could not collect logs for failed managed runtime container {ContainerId}.", started.Id);
                }
            }
            var candidateCleanupWarnings = new List<string>();
            var candidateCleanupConfirmed = await TryStopOwnedContainerAsync(
                started?.Id ?? candidate.ContainerName!,
                appId,
                deploymentTargetId,
                candidateCleanupWarnings,
                "failed runtime candidate");
            foreach (var warning in candidateCleanupWarnings)
                logger.LogWarning("{Warning}", warning);
            try
            {
                if (candidateCleanupConfirmed && previous is not null)
                    await WriteMetadataAsync(previous, CancellationToken.None);
                else if (candidateCleanupConfirmed && File.Exists(metadataPath))
                    File.Delete(metadataPath);
                else
                {
                    candidate.State = RuntimeReadinessState.Failed;
                    candidate.LastError = deploymentError.Message;
                    var retained = previous is null
                        ? CloneMetadata(pending)
                        : CloneMetadata(previous);
                    retained.Candidate = candidate;
                    retained.State = previous?.State ?? RuntimeReadinessState.Failed;
                    await WriteMetadataAsync(retained, CancellationToken.None);
                }
            }
            catch (Exception restoreError)
            {
                logger.LogWarning(restoreError, "Could not restore managed runtime metadata after deployment failure for {AppId}/{TargetId}.", appId, deploymentTargetId);
            }
            throw;
        }
    }

    public async Task StopAsync(Guid appId, Guid deploymentTargetId)
    {
        var key = new DeploymentRuntimeKey(appId, deploymentTargetId);
        ActiveContainer? current;
        lock (gate)
        {
            active.Remove(key, out current);
        }
        registry.Remove(appId, deploymentTargetId);
        var metadataPath = GetMetadataPath(appId, deploymentTargetId);
        var metadata = File.Exists(metadataPath)
            ? await ReadMetadataAsync(metadataPath, CancellationToken.None)
            : null;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(current?.ContainerId)) ids.Add(current.ContainerId);
        if (!string.IsNullOrWhiteSpace(metadata?.ContainerId)) ids.Add(metadata.ContainerId);
        if (!string.IsNullOrWhiteSpace(metadata?.Candidate?.ContainerId)) ids.Add(metadata.Candidate.ContainerId);
        if (!string.IsNullOrWhiteSpace(metadata?.Candidate?.ContainerName)) ids.Add(metadata.Candidate.ContainerName);
        var warnings = new List<string>();
        foreach (var id in ids)
            await TryStopOwnedContainerAsync(id, appId, deploymentTargetId, warnings, "explicit runtime stop");
        foreach (var warning in warnings)
            logger.LogWarning("{Warning}", warning);
        if (warnings.Count > 0)
            throw new InvalidOperationException($"Managed runtime cleanup for {appId}/{deploymentTargetId} is incomplete; target state was preserved for retry. {string.Join(" ", warnings)}");
    }

    private async Task<string?> PromoteAsync(
        Guid appId,
        Guid targetId,
        RunnerRuntimeDeploymentMetadata? previous,
        RunnerRuntimeDeploymentMetadata candidate,
        CancellationToken cancellationToken)
    {
        var key = new DeploymentRuntimeKey(appId, targetId);
        var readyCandidate = CloneMetadata(candidate);
        readyCandidate.State = RuntimeReadinessState.Ready;
        readyCandidate.Candidate = null;
        // This durable candidate record is the recovery point immediately before
        // the irreversible registry/color transition.
        var checkpoint = previous is null
            ? new RunnerRuntimeDeploymentMetadata
            {
                AppId = appId.ToString(),
                DeploymentTargetId = targetId.ToString(),
                Runtime = readyCandidate.Runtime,
                Health = readyCandidate.Health,
                Storage = readyCandidate.Storage,
                State = RuntimeReadinessState.Starting
            }
            : CloneMetadata(previous);
        checkpoint.Candidate = readyCandidate;
        checkpoint.State = RuntimeReadinessState.Starting;
        await WriteMetadataAsync(checkpoint, cancellationToken);
        ActiveContainer? old;
        lock (gate)
        {
            active.TryGetValue(key, out old);
            if (old is null && !string.IsNullOrWhiteSpace(previous?.ContainerId))
                old = new ActiveContainer(appId, targetId, previous.ContainerId!, previous.ContainerName ?? previous.ContainerId!, previous.EffectivePort ?? 0);
            active[key] = new ActiveContainer(appId, targetId, readyCandidate.ContainerId!, readyCandidate.ContainerName!, readyCandidate.EffectivePort!.Value);
            registry.Register(appId, targetId, readyCandidate.EffectivePort.Value);
        }

        var warnings = new List<string>();
        var oldRemoved = string.IsNullOrWhiteSpace(old?.ContainerId);
        if (!string.IsNullOrWhiteSpace(old?.ContainerId))
        {
            oldRemoved = await TryStopOwnedContainerAsync(
                old.ContainerId,
                appId,
                targetId,
                warnings,
                "previous runtime");
            if (oldRemoved && previous?.ArtifactRoot is not null
                && !string.Equals(previous.ArtifactRoot, candidate.ArtifactRoot, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(previous.ArtifactRoot))
            {
                try { Directory.Delete(previous.ArtifactRoot, recursive: true); }
                catch (Exception ex) { warnings.Add($"Previous runtime artifact cleanup deferred: {ex.Message}"); }
            }
        }

        try { await WriteMetadataAsync(readyCandidate, cancellationToken); }
        catch (Exception ex)
        {
            warnings.Add($"Runtime metadata compaction deferred until Runner reconciliation: {ex.Message}");
        }
        return warnings.Count == 0 ? null : string.Join(" ", warnings);
    }

    private async Task<bool> TryStopOwnedContainerAsync(
        string containerIdOrName,
        Guid appId,
        Guid targetId,
        ICollection<string> warnings,
        string description)
    {
        DockerContainerIdentity identity;
        try
        {
            identity = await InspectAsync(appId, targetId, containerIdOrName, CancellationToken.None);
        }
        catch (Exception ex)
        {
            warnings.Add($"{description} cleanup deferred because ownership could not be verified for {containerIdOrName}: {ex.Message}");
            return false;
        }
        if (!identity.Exists)
            return true;
        if (!HasExpectedIdentity(identity, appId, targetId))
        {
            warnings.Add($"Preserved {description} container {containerIdOrName}; Automaton ownership labels did not match.");
            return false;
        }
        try
        {
            await StopAndRemoveAsync(appId, targetId, identity.Id, CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add($"{description} cleanup deferred: {ex.Message}");
            return false;
        }
    }

    private async Task SuperviseAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RetryPendingReconciliationAsync(cancellationToken);
                ActiveContainer[] runtimes;
                lock (gate) runtimes = active.Values.ToArray();
                foreach (var runtime in runtimes)
                {
                    DockerContainerIdentity identity;
                    try { identity = await InspectAsync(runtime.AppId, runtime.TargetId, runtime.ContainerId, cancellationToken); }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Could not inspect managed runtime container {ContainerId}.", runtime.ContainerId);
                        continue;
                    }

                    if (identity.Exists && identity.Running)
                        continue;

                    var key = new DeploymentRuntimeKey(runtime.AppId, runtime.TargetId);
                    lock (gate)
                    {
                        if (!active.TryGetValue(key, out var current)
                            || !string.Equals(current.ContainerId, runtime.ContainerId, StringComparison.Ordinal))
                            continue;
                        active.Remove(key);
                    }
                    registry.Remove(runtime.AppId, runtime.TargetId);
                    try
                    {
                        var path = GetMetadataPath(runtime.AppId, runtime.TargetId);
                        var metadata = await ReadMetadataAsync(path, CancellationToken.None);
                        if (metadata is not null
                            && string.Equals(metadata.ContainerId, runtime.ContainerId, StringComparison.Ordinal))
                        {
                            metadata.State = RuntimeReadinessState.Failed;
                            metadata.EffectivePort = null;
                            metadata.LastError = "The managed runtime container exited unexpectedly.";
                            await WriteMetadataAsync(metadata, CancellationToken.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Could not persist failed state for exited runtime {ContainerId}.", runtime.ContainerId);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RetryPendingReconciliationAsync(CancellationToken cancellationToken)
    {
        string[] paths;
        lock (gate) paths = pendingReconciliation.ToArray();
        foreach (var path in paths)
        {
            try
            {
                await ReconcileMetadataAsync(path, cancellationToken);
                lock (gate) pendingReconciliation.Remove(path);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not retry managed application container reconciliation from {MetadataPath}", path);
            }
        }
    }

    private async Task WaitForReadinessAsync(int port, ApplicationHealthSpec health, CancellationToken cancellationToken)
    {
        var healthPort = port;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(health.StartupTimeoutSeconds));
        using var client = health.Path is null ? null : new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                if (health.Path is null)
                {
                    using var tcp = new TcpClient();
                    await tcp.ConnectAsync(runtimeHostAddress, healthPort, timeout.Token);
                    return;
                }
                using var response = await client!.GetAsync(
                    new UriBuilder("http", runtimeHostAddress, healthPort) { Path = health.Path }.Uri,
                    timeout.Token);
                if ((int)response.StatusCode is >= 200 and < 300)
                    return;
            }
            catch (Exception ex) when ((ex is SocketException or HttpRequestException or TaskCanceledException) && !timeout.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
            }
            try
            {
                await Task.Delay(100, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                break;
            }
        }
        throw new TimeoutException($"Runtime container did not become ready on {runtimeHostAddress}:{healthPort}.");
    }

    private string GetMetadataPath(Guid appId, Guid targetId)
        => Path.Combine(workspace.GetAppAutomationRoot(appId, targetId), MetadataFileName);

    private async Task<RunnerRuntimeDeploymentMetadata?> ReadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<RunnerRuntimeDeploymentMetadata>(
            await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
    }

    private async Task WriteMetadataAsync(RunnerRuntimeDeploymentMetadata metadata, CancellationToken cancellationToken)
    {
        var path = GetMetadataPath(Guid.Parse(metadata.AppId), Guid.Parse(metadata.DeploymentTargetId));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(metadata, JsonOptions), cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    private static RunnerRuntimeDeploymentMetadata CloneMetadata(RunnerRuntimeDeploymentMetadata metadata)
        => JsonSerializer.Deserialize<RunnerRuntimeDeploymentMetadata>(
            JsonSerializer.Serialize(metadata, JsonOptions), JsonOptions)
            ?? throw new InvalidOperationException("Could not clone runtime metadata.");

    private void Register(Guid appId, Guid targetId, string id, string? name, int? port)
    {
        if (port is not > 0) return;
        lock (gate) active[new DeploymentRuntimeKey(appId, targetId)] = new ActiveContainer(appId, targetId, id, name ?? id, port.Value);
        registry.Register(appId, targetId, port.Value);
    }

    private int AllocatePort(int requested)
    {
        if (requested is >= 1 and <= 65535
            && requested != RunnerPlatformPorts.GetRuntimeGatewayPort()
            && !registry.IsPortRegistered(requested)
            && CanBind(requested))
            return requested;
        for (var port = portStart; port <= portEnd; port++)
        {
            if (port != RunnerPlatformPorts.GetRuntimeGatewayPort()
                && !registry.IsPortRegistered(port)
                && CanBind(port)) return port;
        }
        throw new InvalidOperationException("No managed runtime ports are available.");
    }

    private bool CanBind(int port)
    {
        if (!canProbeRuntimeHost)
            return true;
        try
        {
            using var listener = new TcpListener(IPAddress.Parse(runtimeHostAddress), port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private string ValidateArtifactRoot(Guid appId, Guid targetId, string path)
    {
        var candidateRoot = Path.GetFullPath(workspace.GetAppRuntimeCandidateDirectory(appId, targetId, Guid.NewGuid()));
        var artifact = Path.GetFullPath(path);
        var candidatesRoot = Path.GetDirectoryName(candidateRoot)!;
        if (!artifact.StartsWith(candidatesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Application artifact is outside the managed candidate boundary.");
        return artifact;
    }

    private static int ReadPort(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

    private static bool HasExpectedIdentity(DockerContainerIdentity identity, Guid appId, Guid targetId)
        => identity.Labels.TryGetValue("com.automaton.managed", out var managed)
            && string.Equals(managed, "true", StringComparison.OrdinalIgnoreCase)
            && identity.Labels.TryGetValue("com.automaton.app-id", out var actualApp)
            && string.Equals(actualApp, appId.ToString(), StringComparison.OrdinalIgnoreCase)
            && identity.Labels.TryGetValue("com.automaton.deployment-target-id", out var actualTarget)
            && string.Equals(actualTarget, targetId.ToString(), StringComparison.OrdinalIgnoreCase)
            && identity.Labels.TryGetValue("com.automaton.runtime-kind", out var kind)
            && string.Equals(kind, "generic", StringComparison.OrdinalIgnoreCase);

    private static bool IsOciContainerMetadata(RunnerRuntimeDeploymentMetadata? metadata)
        => metadata is not null
            && !string.IsNullOrWhiteSpace(metadata.ContainerId)
            && !string.IsNullOrWhiteSpace(metadata.Runtime?.Image);

    private static bool IsOciCandidateMetadata(RunnerRuntimeDeploymentMetadata? metadata)
        => metadata is not null
            && (!string.IsNullOrWhiteSpace(metadata.ContainerId) || !string.IsNullOrWhiteSpace(metadata.ContainerName))
            && !string.IsNullOrWhiteSpace(metadata.Runtime?.Image);

    private static ApplicationRuntimeSpec CloneRuntimeWithResolvedImage(
        ApplicationRuntimeSpec? runtime,
        string? resolvedImage)
    {
        if (runtime is null)
            throw new InvalidOperationException("Persisted runtime metadata is incomplete.");
        return new ApplicationRuntimeSpec
        {
            Image = string.IsNullOrWhiteSpace(resolvedImage) ? runtime.Image : resolvedImage,
            Command = runtime.Command,
            Port = runtime.Port
        };
    }

    private Task<DockerContainerIdentity> InspectAsync(Guid appId, Guid targetId, string id, CancellationToken cancellationToken)
        => containers is IScopedApplicationContainerRuntime scoped
            ? scoped.InspectAsync(new(appId, targetId), id, cancellationToken)
            : containers.InspectAsync(id, cancellationToken);

    private Task<bool> IsRunningAsync(Guid appId, Guid targetId, string id, CancellationToken cancellationToken)
        => containers is IScopedApplicationContainerRuntime scoped
            ? scoped.IsRunningAsync(new(appId, targetId), id, cancellationToken)
            : containers.IsRunningAsync(id, cancellationToken);

    private Task<string> GetLogsAsync(Guid appId, Guid targetId, string id, CancellationToken cancellationToken)
        => containers is IScopedApplicationContainerRuntime scoped
            ? scoped.GetLogsAsync(new(appId, targetId), id, cancellationToken)
            : containers.GetLogsAsync(id, cancellationToken);

    private Task StopAndRemoveAsync(Guid appId, Guid targetId, string id, CancellationToken cancellationToken)
        => containers is IScopedApplicationContainerRuntime scoped
            ? scoped.StopAndRemoveAsync(new(appId, targetId), id, cancellationToken)
            : containers.StopAndRemoveAsync(id, cancellationToken);

    private sealed record ActiveContainer(Guid AppId, Guid TargetId, string ContainerId, string ContainerName, int Port);
}
