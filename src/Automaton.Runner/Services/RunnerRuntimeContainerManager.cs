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

        var candidateIsOci = IsOciContainerMetadata(metadata.Candidate);
        var currentIsOci = IsOciContainerMetadata(metadata);
        if (!candidateIsOci && !currentIsOci)
            return;

        var candidate = metadata.Candidate;
        if (candidateIsOci && candidate is not null)
        {
            var candidateIdentity = await containers.InspectAsync(candidate.ContainerId!, cancellationToken);
            if (candidateIdentity.Exists
                && candidateIdentity.Running
                && HasExpectedIdentity(candidateIdentity, appId, targetId)
                && string.Equals(candidate.State, RuntimeReadinessState.Ready, StringComparison.Ordinal))
            {
                if (currentIsOci
                    && !string.IsNullOrWhiteSpace(metadata.ContainerId)
                    && !string.Equals(metadata.ContainerId, candidate.ContainerId, StringComparison.Ordinal))
                {
                    var previousIdentity = await containers.InspectAsync(metadata.ContainerId, cancellationToken);
                    if (previousIdentity.Exists && HasExpectedIdentity(previousIdentity, appId, targetId))
                        await containers.StopAndRemoveAsync(previousIdentity.Id, CancellationToken.None);
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
                try { await containers.StopAndRemoveAsync(candidateIdentity.Id, CancellationToken.None); }
                catch (Exception ex) { logger.LogWarning(ex, "Could not remove invalid managed runtime candidate {ContainerId}.", candidateIdentity.Id); }
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
        var identity = await containers.InspectAsync(recorded, cancellationToken);
        if (identity.Exists
            && identity.Running
            && HasExpectedIdentity(identity, appId, targetId))
        {
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
            metadata.ArtifactRoot);
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
        string? artifactRoot = null)
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
        var effectivePort = AllocatePort(containerPort);
        var artifact = artifactRoot ?? workspace.GetAppRuntimeCandidateApplicationDirectory(
            appId, deploymentTargetId, Guid.NewGuid());
        artifact = ValidateArtifactRoot(appId, deploymentTargetId, artifact);
        Directory.CreateDirectory(artifact);
        var storage = workspace.PrepareApplicationStorage(appId, contract.Storage);
        var containerName = $"automaton-app-{appId:N}-{deploymentTargetId:N}-{Guid.NewGuid():N}";
        DockerApplicationContainer? started = null;
        try
        {
            started = await containers.StartApplicationAsync(
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
                    deploymentTargetId),
                cancellationToken);

            var candidate = new RunnerRuntimeDeploymentMetadata
            {
                AppId = appId.ToString(),
                DeploymentTargetId = deploymentTargetId.ToString(),
                Runtime = contract.Runtime,
                Health = contract.Health,
                Storage = contract.Storage,
                ArtifactRoot = artifact,
                ArtifactId = Path.GetFileName(Path.GetDirectoryName(artifact) ?? artifact),
                ContainerId = started.Id,
                ContainerName = started.Name,
                RequestedRuntimeImage = started.Image.RequestedReference,
                ResolvedRuntimeImage = started.Image.ResolvedReference,
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

            await WaitForReadinessAsync(effectivePort, contract.Health, cancellationToken);
            if (!await containers.IsRunningAsync(started.Id, cancellationToken))
                throw new InvalidOperationException("The runtime container exited after readiness and before promotion.");
            var warning = await PromoteAsync(appId, deploymentTargetId, previous, candidate, cancellationToken);
            return new RuntimeApplyResult { PromotionIrreversible = true, Warning = warning };
        }
        catch
        {
            if (started is not null)
            {
                try
                {
                    var logs = await containers.GetLogsAsync(started.Id, CancellationToken.None);
                    logger.LogWarning(
                        "Managed application container {ContainerId} failed before promotion. Logs: {Logs}",
                        started.Id,
                        logs);
                }
                catch (Exception cleanupError)
                {
                    logger.LogWarning(cleanupError, "Could not collect logs for failed managed runtime container {ContainerId}.", started.Id);
                }
                try
                {
                    await containers.StopAndRemoveAsync(started.Id, CancellationToken.None);
                }
                catch (Exception cleanupError)
                {
                    logger.LogWarning(cleanupError, "Could not remove failed managed runtime container {ContainerId}.", started.Id);
                }
            }
            try
            {
                if (previous is not null)
                    await WriteMetadataAsync(previous, CancellationToken.None);
                else if (File.Exists(metadataPath))
                    File.Delete(metadataPath);
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
        var id = current?.ContainerId ?? metadata?.ContainerId;
        if (!string.IsNullOrWhiteSpace(id))
            await containers.StopAndRemoveAsync(id, CancellationToken.None);
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
            try { await containers.StopAndRemoveAsync(old.ContainerId, CancellationToken.None); oldRemoved = true; }
            catch (Exception ex) { warnings.Add($"Previous runtime cleanup deferred: {ex.Message}"); }
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
                    try { identity = await containers.InspectAsync(runtime.ContainerId, cancellationToken); }
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
                    await tcp.ConnectAsync(IPAddress.Loopback, healthPort, timeout.Token);
                    return;
                }
                using var response = await client!.GetAsync(
                    new UriBuilder("http", IPAddress.Loopback.ToString(), healthPort) { Path = health.Path }.Uri,
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
        throw new TimeoutException($"Runtime container did not become ready on 127.0.0.1:{healthPort}.");
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

    private static bool CanBind(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
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

    private sealed record ActiveContainer(Guid AppId, Guid TargetId, string ContainerId, string ContainerName, int Port);
}
