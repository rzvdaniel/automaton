using Automaton.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Supervises managed application containers. The Runner owns lifecycle and
/// readiness; the application image owns the process and its HTTP behavior.
/// </summary>
public sealed class RunnerRuntimeContainerManager : IAppRuntimeProcessManager
{
    private readonly RunnerWorkspaceService workspace;
    private readonly RunnerRuntimeMetadataStore metadataStore;
    private readonly RunnerRuntimeNetworkPolicy networkPolicy;
    private readonly RunnerRuntimeRegistry registry;
    private readonly IApplicationContainerRuntime containers;
    private readonly ILogger<RunnerRuntimeContainerManager> logger;
    private readonly object gate = new();
    private readonly Dictionary<DeploymentRuntimeKey, ActiveContainer> active = [];
    private readonly HashSet<string> pendingReconciliation = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource supervisionCts = new();
    private int disposed;
    private volatile bool acceptingLifecycleWork;
    private Task? supervisionTask;

    public RunnerRuntimeContainerManager(
        RunnerWorkspaceService workspace,
        RunnerRuntimeRegistry registry,
        IApplicationContainerRuntime containers,
        ILogger<RunnerRuntimeContainerManager> logger)
    {
        this.workspace = workspace;
        metadataStore = new RunnerRuntimeMetadataStore(workspace);
        this.registry = registry;
        networkPolicy = new RunnerRuntimeNetworkPolicy(registry);
        this.containers = containers;
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        acceptingLifecycleWork = true;
        // Brokered runtimes depend on the authenticated Hub connection. Defer
        // their recovery until supervision is running so host startup can
        // complete and establish that connection first.
        var deferReconciliation = containers is IScopedApplicationContainerRuntime;
        foreach (var metadataPath in workspace.EnumerateDeploymentMetadataPaths())
        {
            if (deferReconciliation)
            {
                lock (gate) pendingReconciliation.Add(metadataPath);
                continue;
            }
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
        var metadata = await metadataStore.ReadAsync(metadataPath, cancellationToken);
        if (metadata is null)
            return;
        var (appId, targetId) = ParseDeploymentIdentifiers(metadata);
        var currentIsOci = RunnerRuntimeMetadataPolicy.IsOciContainer(metadata);
        var candidateIsOci = RunnerRuntimeMetadataPolicy.IsOciCandidate(metadata.Candidate);
        if (!candidateIsOci && !currentIsOci) return;
        if (await ReconcileCandidateAsync(metadata, appId, targetId, currentIsOci, cancellationToken)) return;
        await ReconcileCurrentContainerAsync(metadata, appId, targetId, currentIsOci, cancellationToken);
    }

    private static (Guid AppId, Guid TargetId) ParseDeploymentIdentifiers(RunnerRuntimeDeploymentMetadata metadata)
    {
        if (!Guid.TryParse(metadata.AppId, out var appId)
            || !Guid.TryParse(metadata.DeploymentTargetId, out var targetId))
            throw new InvalidOperationException("Runtime deployment metadata contains invalid identifiers.");
        return (appId, targetId);
    }

    private async Task<bool> ReconcileCandidateAsync(
        RunnerRuntimeDeploymentMetadata metadata,
        Guid appId,
        Guid targetId,
        bool currentIsOci,
        CancellationToken cancellationToken)
    {
        var candidate = metadata.Candidate;
        if (!RunnerRuntimeMetadataPolicy.IsOciCandidate(candidate) || candidate is null) return false;
        if (RunnerRuntimeMetadataPolicy.HasComposeMetadata(candidate) && string.IsNullOrWhiteSpace(candidate.ContainerId))
        {
            if (candidate.ComposeCandidateId is not Guid composeCandidateId)
                throw new InvalidOperationException("Compose candidate recovery requires the typed project runtime and candidate identity.");
            var contract = RunnerRuntimeMetadataPolicy.CreateApplicationContract(candidate);
            var resumed = await containers.InspectComposeProjectAsync(
                appId, targetId, composeCandidateId, contract, cancellationToken);
            if (resumed is null)
            {
                var missing = new DockerContainerIdentity(string.Empty, false, false, new Dictionary<string, string>());
                await DiscardInvalidCandidateAsync(metadata, candidate, missing, appId, targetId, cancellationToken);
                return false;
            }
            candidate.ContainerId = resumed.Id;
            candidate.ContainerName = resumed.Name;
            candidate.ResolvedRuntimeImage = resumed.Image.ResolvedReference;
            candidate.EffectivePort = resumed.HostPort;
            await metadataStore.WriteAsync(metadata, cancellationToken);
        }
        var identity = await InspectAsync(appId, targetId, candidate.ContainerId ?? candidate.ContainerName!, cancellationToken);
        if (await PromoteReconciledCandidateAsync(metadata, candidate, identity, appId, targetId, currentIsOci, cancellationToken))
            return true;
        await DiscardInvalidCandidateAsync(metadata, candidate, identity, appId, targetId, cancellationToken);
        return false;
    }

    private async Task<bool> PromoteReconciledCandidateAsync(
        RunnerRuntimeDeploymentMetadata metadata,
        RunnerRuntimeDeploymentMetadata candidate,
        DockerContainerIdentity identity,
        Guid appId,
        Guid targetId,
        bool currentIsOci,
        CancellationToken cancellationToken)
    {
        if (!identity.Exists || !identity.Running || !RunnerRuntimeMetadataPolicy.HasExpectedIdentity(identity, appId, targetId)
            || !string.Equals(candidate.State, RuntimeReadinessState.Ready, StringComparison.Ordinal))
            return false;
        if (identity.HostPort is > 0) candidate.EffectivePort = identity.HostPort;
        await RemovePreviousCandidateContainerAsync(metadata, candidate, appId, targetId, currentIsOci, cancellationToken);
        var promoted = metadataStore.Clone(candidate);
        promoted.Candidate = null;
        promoted.State = RuntimeReadinessState.Ready;
        await metadataStore.WriteAsync(promoted, cancellationToken);
        Register(appId, targetId, identity.Id, promoted.ContainerName, promoted.EffectivePort, promoted.ComposeCandidateId);
        return true;
    }

    private async Task RemovePreviousCandidateContainerAsync(
        RunnerRuntimeDeploymentMetadata metadata,
        RunnerRuntimeDeploymentMetadata candidate,
        Guid appId,
        Guid targetId,
        bool currentIsOci,
        CancellationToken cancellationToken)
    {
        if (!currentIsOci || string.IsNullOrWhiteSpace(metadata.ContainerId)
            || string.Equals(metadata.ContainerId, candidate.ContainerId, StringComparison.Ordinal)) return;
        if (metadata.ComposeCandidateId is Guid composeCandidateId)
        {
            try
            {
                await containers.RemoveComposeProjectAsync(new(appId, targetId), composeCandidateId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not remove previous managed Compose project {CandidateId}.", composeCandidateId);
            }
            return;
        }
        var previous = await InspectAsync(appId, targetId, metadata.ContainerId, cancellationToken);
        if (previous.Exists && RunnerRuntimeMetadataPolicy.HasExpectedIdentity(previous, appId, targetId))
            await StopAndRemoveAsync(appId, targetId, previous.Id, CancellationToken.None);
        else
            logger.LogWarning("Preserving previous runtime container {ContainerId} because its Automaton ownership labels could not be verified.", metadata.ContainerId);
    }

    private async Task DiscardInvalidCandidateAsync(
        RunnerRuntimeDeploymentMetadata metadata,
        RunnerRuntimeDeploymentMetadata candidate,
        DockerContainerIdentity identity,
        Guid appId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        if (candidate.ComposeCandidateId is Guid composeCandidateId)
        {
            await containers.RemoveComposeProjectAsync(new(appId, targetId), composeCandidateId, CancellationToken.None);
        }
        else if (identity.Exists && !RunnerRuntimeMetadataPolicy.HasExpectedIdentity(identity, appId, targetId))
            throw new InvalidOperationException($"Refusing to reconcile candidate container '{candidate.ContainerId}' without verified Automaton ownership labels.");
        else if (identity.Exists)
        {
            logger.LogWarning("Discarding an uncommitted managed runtime candidate {ContainerId}.", candidate.ContainerId);
            try
            {
                await StopAndRemoveAsync(appId, targetId, identity.Id, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not remove invalid managed runtime candidate {ContainerId}; retaining metadata for retry.", identity.Id);
                throw;
            }
        }
        metadata.Candidate = null;
        await metadataStore.WriteAsync(metadata, cancellationToken);
    }

    private async Task ReconcileCurrentContainerAsync(
        RunnerRuntimeDeploymentMetadata metadata,
        Guid appId,
        Guid targetId,
        bool currentIsOci,
        CancellationToken cancellationToken)
    {
        if (!currentIsOci || string.IsNullOrWhiteSpace(metadata.ContainerId)) return;
        var identity = await InspectAsync(appId, targetId, metadata.ContainerId, cancellationToken);
        if (identity.Exists && identity.Running && RunnerRuntimeMetadataPolicy.HasExpectedIdentity(identity, appId, targetId))
        {
            if (identity.HostPort is > 0) metadata.EffectivePort = identity.HostPort;
            await metadataStore.WriteAsync(metadata, cancellationToken);
            Register(appId, targetId, identity.Id, metadata.ContainerName, metadata.EffectivePort, metadata.ComposeCandidateId);
            return;
        }
        logger.LogWarning("Refusing to adopt container {ContainerId}: it is missing, stopped, or does not have the expected Automaton identity.", metadata.ContainerId);
        if (!workspace.RuntimeArtifactExists(metadata.ArtifactRoot)) return;
        await ApplyAsync(appId, targetId, RunnerRuntimeMetadataPolicy.CreateApplicationContract(metadata), cancellationToken,
            metadata.ArtifactRoot, metadata.CandidateId);
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        supervisionCts.Cancel();
        supervisionCts.Dispose();
    }

    public async Task<RuntimeApplyResult> ApplyAsync(
        Guid appId,
        Guid deploymentTargetId,
        ApplicationContract contract,
        CancellationToken cancellationToken = default,
        string? artifactRoot = null,
        Guid? candidateId = null,
        Guid? deploymentAttemptId = null)
    {
        RunnerRuntimeMetadataPolicy.ValidateApplyRequest(contract, acceptingLifecycleWork);
        var metadataPath = metadataStore.GetPath(appId, deploymentTargetId);
        var previous = await ReadExistingMetadataAsync(metadataPath, cancellationToken);
        if (RunnerRuntimeMetadataPolicy.IsStoppedContract(contract))
            return await StopConfiguredRuntimeAsync(appId, deploymentTargetId, contract, cancellationToken);

        var attempt = await PrepareRuntimeCandidateAsync(
            appId, deploymentTargetId, contract, previous, metadataPath, artifactRoot, candidateId, deploymentAttemptId, cancellationToken);
        return await StartAndPromoteCandidateAsync(attempt, previous, cancellationToken);
    }

    private async Task<RunnerRuntimeDeploymentMetadata?> ReadExistingMetadataAsync(
        string metadataPath,
        CancellationToken cancellationToken)
        => File.Exists(metadataPath) ? await metadataStore.ReadAsync(metadataPath, cancellationToken) : null;

    private async Task<RuntimeApplyResult> StopConfiguredRuntimeAsync(
        Guid appId,
        Guid targetId,
        ApplicationContract contract,
        CancellationToken cancellationToken)
    {
        await StopAsync(appId, targetId);
        await metadataStore.WriteAsync(CreateStoppedMetadata(appId, targetId, contract), cancellationToken);
        return new RuntimeApplyResult();
    }

    private static RunnerRuntimeDeploymentMetadata CreateStoppedMetadata(
        Guid appId,
        Guid targetId,
        ApplicationContract contract)
        => new()
        {
            AppId = appId.ToString(),
            DeploymentTargetId = targetId.ToString(),
            Runtime = contract.Runtime,
            Health = contract.Health,
            Storage = contract.Storage,
            ComposeJson = RunnerRuntimeMetadataPolicy.SerializeCompose(contract.Compose),
            State = RuntimeReadinessState.Stopped
        };

    private async Task<RuntimeApplyAttempt> PrepareRuntimeCandidateAsync(
        Guid appId,
        Guid targetId,
        ApplicationContract contract,
        RunnerRuntimeDeploymentMetadata? previous,
        string metadataPath,
        string? artifactRoot,
        Guid? candidateId,
        Guid? deploymentAttemptId,
        CancellationToken cancellationToken)
    {
        var runtimeImage = RunnerRuntimeMetadataPolicy.RuntimeImage(contract);
        var scopedRuntime = containers as IScopedApplicationContainerRuntime;
        var effectivePort = scopedRuntime is null ? networkPolicy.AllocatePort(contract.Runtime.Port) : 0;
        var resolvedCandidateId = candidateId ?? Guid.NewGuid();
        var artifact = artifactRoot ?? workspace.GetAppRuntimeCandidateApplicationDirectory(appId, targetId, resolvedCandidateId);
        artifact = workspace.ValidateRuntimeArtifactRoot(appId, targetId, artifact);
        workspace.CreateRuntimeArtifactRoot(artifact);
        var storage = workspace.PrepareApplicationStorage(appId, contract.Storage);
        var candidate = CreateCandidateMetadata(appId, targetId, contract, artifact, resolvedCandidateId, deploymentAttemptId, runtimeImage, effectivePort);
        var pending = CreatePendingMetadata(appId, targetId, contract, previous, candidate);
        await metadataStore.WriteAsync(pending, cancellationToken);
        return new RuntimeApplyAttempt(
            appId, targetId, contract, metadataPath, artifact, storage, runtimeImage, effectivePort,
            resolvedCandidateId, deploymentAttemptId, scopedRuntime, candidate, pending, previous);
    }

    private RunnerRuntimeDeploymentMetadata CreateCandidateMetadata(
        Guid appId,
        Guid targetId,
        ApplicationContract contract,
        string artifact,
        Guid candidateId,
        Guid? deploymentAttemptId,
        string runtimeImage,
        int effectivePort)
        => new()
        {
            AppId = appId.ToString(),
            DeploymentTargetId = targetId.ToString(),
            Runtime = contract.Runtime,
            Health = contract.Health,
            Storage = contract.Storage,
            ComposeJson = RunnerRuntimeMetadataPolicy.SerializeCompose(contract.Compose),
            ComposeProjectId = contract.Compose is null ? null : candidateId.ToString("N"),
            ComposeCandidateId = contract.Compose is null ? null : candidateId,
            DeploymentAttemptId = deploymentAttemptId,
            ArtifactRoot = artifact,
            ArtifactId = workspace.GetRuntimeArtifactId(artifact),
            CandidateId = candidateId,
            ContainerName = $"automaton-app-{appId:N}-{targetId:N}-{Guid.NewGuid():N}",
            RequestedRuntimeImage = runtimeImage,
            EffectivePort = effectivePort,
            State = RuntimeReadinessState.Starting
        };

    private RunnerRuntimeDeploymentMetadata CreatePendingMetadata(
        Guid appId,
        Guid targetId,
        ApplicationContract contract,
        RunnerRuntimeDeploymentMetadata? previous,
        RunnerRuntimeDeploymentMetadata candidate)
    {
        var pending = previous is null
            ? new RunnerRuntimeDeploymentMetadata
            {
                AppId = appId.ToString(),
                DeploymentTargetId = targetId.ToString(),
                Runtime = contract.Runtime,
                Health = contract.Health,
                Storage = contract.Storage,
                ComposeJson = RunnerRuntimeMetadataPolicy.SerializeCompose(contract.Compose),
                State = RuntimeReadinessState.Starting
            }
            : metadataStore.Clone(previous);
        pending.Candidate = candidate;
        pending.State = RuntimeReadinessState.Starting;
        return pending;
    }

    private async Task<RuntimeApplyResult> StartAndPromoteCandidateAsync(
        RuntimeApplyAttempt attempt,
        RunnerRuntimeDeploymentMetadata? previous,
        CancellationToken cancellationToken)
    {
        DockerApplicationContainer? started = null;
        try
        {
            started = await CreateCandidateContainerAsync(attempt, cancellationToken);
            await StartCandidateRuntimeAsync(attempt, started, cancellationToken);
            await networkPolicy.WaitForReadinessAsync(attempt.EffectivePort, attempt.Contract.Health, cancellationToken);
            if (!await IsRunningAsync(attempt.AppId, attempt.TargetId, started.Id, cancellationToken))
                throw new InvalidOperationException("The runtime container exited after readiness and before promotion.");
            var warning = await PromoteAsync(attempt.AppId, attempt.TargetId, previous, attempt.Candidate, cancellationToken);
            return new RuntimeApplyResult { PromotionIrreversible = true, Warning = warning };
        }
        catch (Exception deploymentError)
        {
            await RestoreFailedCandidateAsync(attempt, previous, started, deploymentError);
            throw;
        }
    }

    private async Task<DockerApplicationContainer> CreateCandidateContainerAsync(
        RuntimeApplyAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (RunnerRuntimeMetadataPolicy.HasComposeContract(attempt.Contract))
        {
            var composeCandidate = await containers.CreateComposeProjectAsync(
                attempt.AppId, attempt.TargetId, attempt.CandidateId, attempt.Contract,
                attempt.DeploymentAttemptId, cancellationToken);
            attempt.Candidate.ContainerId = composeCandidate.Id;
            attempt.Candidate.ContainerName = composeCandidate.Name;
            attempt.Candidate.ResolvedRuntimeImage = composeCandidate.Image.ResolvedReference;
            attempt.Candidate.EffectivePort = composeCandidate.HostPort;
            await metadataStore.WriteAsync(attempt.Pending, cancellationToken);
            return composeCandidate;
        }
        if (containers is not IStagedApplicationContainerRuntime stagedContainers)
            throw new InvalidOperationException("Managed application lifecycle requires the staged Docker container boundary.");
        var command = attempt.Contract.Runtime.Command!;
        var started = await stagedContainers.CreateApplicationAsync(
            new DockerApplicationContainerRequest(
                attempt.Candidate.ContainerName!, attempt.RuntimeImage, attempt.Artifact,
                command.WorkingDirectory, command, attempt.Contract.Runtime.Port, attempt.EffectivePort,
                attempt.Storage.DataDirectory, attempt.Storage.Mounts, attempt.AppId, attempt.TargetId,
                attempt.CandidateId, attempt.Contract.Storage, attempt.Contract.Runtime.User),
            cancellationToken);
        attempt.Candidate.ContainerId = started.Id;
        attempt.Candidate.ContainerName = started.Name;
        attempt.Candidate.RequestedRuntimeImage = started.Image.RequestedReference;
        attempt.Candidate.ResolvedRuntimeImage = started.Image.ResolvedReference;
        await metadataStore.WriteAsync(attempt.Pending, cancellationToken);
        return started;
    }

    private async Task StartCandidateRuntimeAsync(
        RuntimeApplyAttempt attempt,
        DockerApplicationContainer started,
        CancellationToken cancellationToken)
    {
        if (RunnerRuntimeMetadataPolicy.HasComposeContract(attempt.Contract))
        {
            attempt.EffectivePort = started.HostPort
                ?? throw new InvalidOperationException("The Compose public service did not return a routed endpoint.");
            attempt.Candidate.EffectivePort = attempt.EffectivePort;
            await metadataStore.WriteAsync(attempt.Pending, cancellationToken);
            return;
        }
        if (attempt.ScopedRuntime is null)
        {
            var stagedContainers = (IStagedApplicationContainerRuntime)containers;
            await stagedContainers.StartApplicationAsync(started.Id, cancellationToken);
        }
        else
        {
            var endpoint = await attempt.ScopedRuntime.StartApplicationAsync(
                new(attempt.AppId, attempt.TargetId), started.Id, cancellationToken);
            attempt.Candidate.EffectivePort = endpoint.HostPort;
            attempt.EffectivePort = endpoint.HostPort;
            await metadataStore.WriteAsync(attempt.Pending, cancellationToken);
        }
    }

    private async Task RestoreFailedCandidateAsync(
        RuntimeApplyAttempt attempt,
        RunnerRuntimeDeploymentMetadata? previous,
        DockerApplicationContainer? started,
        Exception deploymentError)
    {
        await LogFailedCandidateAsync(attempt, started);
        var cleanupWarnings = new List<string>();
        var cleaned = attempt.Candidate.ComposeCandidateId is Guid composeCandidateId
            ? await TryRemoveComposeProjectAsync(composeCandidateId, attempt.AppId, attempt.TargetId,
                cleanupWarnings, "failed runtime candidate")
            : await TryStopOwnedContainerAsync(
                started?.Id ?? attempt.Candidate.ContainerName!, attempt.AppId, attempt.TargetId,
                cleanupWarnings, "failed runtime candidate");
        foreach (var warning in cleanupWarnings) logger.LogWarning("{Warning}", warning);
        try
        {
            await RestoreMetadataAfterFailureAsync(attempt, previous, cleaned, deploymentError);
        }
        catch (Exception restoreError)
        {
            logger.LogWarning(restoreError,
                "Could not restore managed runtime metadata after deployment failure for {AppId}/{TargetId}.",
                attempt.AppId, attempt.TargetId);
        }
    }

    private async Task LogFailedCandidateAsync(RuntimeApplyAttempt attempt, DockerApplicationContainer? started)
    {
        if (started is null) return;
        try
        {
            var logs = await GetLogsAsync(attempt.AppId, attempt.TargetId, started.Id, CancellationToken.None);
            logger.LogWarning("Managed application container {ContainerId} failed before promotion. Logs: {Logs}", started.Id, logs);
        }
        catch (Exception cleanupError)
        {
            logger.LogWarning(cleanupError, "Could not collect logs for failed managed runtime container {ContainerId}.", started.Id);
        }
    }

    private async Task RestoreMetadataAfterFailureAsync(
        RuntimeApplyAttempt attempt,
        RunnerRuntimeDeploymentMetadata? previous,
        bool candidateCleanupConfirmed,
        Exception deploymentError)
    {
        if (candidateCleanupConfirmed && previous is not null)
        {
            await metadataStore.WriteAsync(previous, CancellationToken.None);
            return;
        }
        if (candidateCleanupConfirmed && File.Exists(attempt.MetadataPath))
        {
            File.Delete(attempt.MetadataPath);
            return;
        }

        attempt.Candidate.State = RuntimeReadinessState.Failed;
        attempt.Candidate.LastError = deploymentError.Message;
        var retained = previous is null ? metadataStore.Clone(attempt.Pending) : metadataStore.Clone(previous);
        retained.Candidate = attempt.Candidate;
        retained.State = previous?.State ?? RuntimeReadinessState.Failed;
        await metadataStore.WriteAsync(retained, CancellationToken.None);
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
        var metadataPath = metadataStore.GetPath(appId, deploymentTargetId);
        var metadata = File.Exists(metadataPath)
            ? await metadataStore.ReadAsync(metadataPath, CancellationToken.None)
            : null;
        var warnings = new List<string>();
        await TryRemoveComposeProjectAsync(current?.ComposeCandidateId ?? metadata?.ComposeCandidateId,
            appId, deploymentTargetId, warnings, "explicit runtime stop");
        await TryRemoveComposeProjectAsync(metadata?.Candidate?.ComposeCandidateId,
            appId, deploymentTargetId, warnings, "pending runtime candidate");
        var ids = GetCleanupContainerReferences(current?.ContainerId, metadata);
        if (containers is IApplicationContainerCleanupRuntime cleanup)
            await cleanup.RemoveTargetContainersAsync(new(appId, deploymentTargetId));
        foreach (var id in ids)
            await TryStopOwnedContainerAsync(id, appId, deploymentTargetId, warnings, "explicit runtime stop");
        foreach (var warning in warnings)
            logger.LogWarning("{Warning}", warning);
        if (warnings.Count > 0)
            throw new InvalidOperationException($"Managed runtime cleanup for {appId}/{deploymentTargetId} is incomplete; target state was preserved for retry. {string.Join(" ", warnings)}");
    }

    private static HashSet<string> GetCleanupContainerReferences(
        string? currentId, RunnerRuntimeDeploymentMetadata? metadata)
        => new[] { currentId, metadata?.ContainerId, metadata?.Candidate?.ContainerId,
            metadata?.Candidate?.ContainerName }.Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!).ToHashSet(StringComparer.Ordinal);

    private async Task<string?> PromoteAsync(
        Guid appId,
        Guid targetId,
        RunnerRuntimeDeploymentMetadata? previous,
        RunnerRuntimeDeploymentMetadata candidate,
        CancellationToken cancellationToken)
    {
        var readyCandidate = metadataStore.Clone(candidate);
        readyCandidate.State = RuntimeReadinessState.Ready;
        readyCandidate.Candidate = null;
        await WritePromotionCheckpointAsync(appId, targetId, previous, readyCandidate, cancellationToken);
        var old = PromoteInMemory(appId, targetId, previous, readyCandidate);
        var warnings = await RemovePreviousRuntimeAsync(appId, targetId, old, previous, candidate);
        await CompactReadyMetadataAsync(readyCandidate, cancellationToken, warnings);
        return warnings.Count == 0 ? null : string.Join(" ", warnings);
    }

    private async Task WritePromotionCheckpointAsync(
        Guid appId,
        Guid targetId,
        RunnerRuntimeDeploymentMetadata? previous,
        RunnerRuntimeDeploymentMetadata readyCandidate,
        CancellationToken cancellationToken)
    {
        // This durable candidate record is the recovery point immediately before
        // the irreversible registry/color transition.
        var checkpoint = previous is null
            ? CreateStartingMetadata(appId, targetId, readyCandidate)
            : metadataStore.Clone(previous);
        checkpoint.Candidate = readyCandidate;
        checkpoint.State = RuntimeReadinessState.Starting;
        await metadataStore.WriteAsync(checkpoint, cancellationToken);
    }

    private static RunnerRuntimeDeploymentMetadata CreateStartingMetadata(
        Guid appId,
        Guid targetId,
        RunnerRuntimeDeploymentMetadata readyCandidate)
        => new()
        {
            AppId = appId.ToString(),
            DeploymentTargetId = targetId.ToString(),
            Runtime = readyCandidate.Runtime,
            Health = readyCandidate.Health,
            Storage = readyCandidate.Storage,
            ComposeJson = readyCandidate.ComposeJson,
            ComposeProjectId = readyCandidate.ComposeProjectId,
            ComposeCandidateId = readyCandidate.ComposeCandidateId,
            State = RuntimeReadinessState.Starting
        };

    private ActiveContainer? PromoteInMemory(
        Guid appId,
        Guid targetId,
        RunnerRuntimeDeploymentMetadata? previous,
        RunnerRuntimeDeploymentMetadata readyCandidate)
    {
        var key = new DeploymentRuntimeKey(appId, targetId);
        lock (gate)
        {
            active.TryGetValue(key, out var old);
            old ??= CreatePreviousRuntime(appId, targetId, previous);
            active[key] = new ActiveContainer(
                appId, targetId, readyCandidate.ContainerId!, readyCandidate.ContainerName!, readyCandidate.EffectivePort!.Value,
                readyCandidate.ComposeCandidateId);
            registry.Register(appId, targetId, readyCandidate.EffectivePort.Value);
            return old;
        }
    }

    private static ActiveContainer? CreatePreviousRuntime(
        Guid appId,
        Guid targetId,
        RunnerRuntimeDeploymentMetadata? previous)
        => string.IsNullOrWhiteSpace(previous?.ContainerId)
            ? null
            : new ActiveContainer(
                appId,
                targetId,
                previous.ContainerId!,
                previous.ContainerName ?? previous.ContainerId!,
                previous.EffectivePort ?? 0,
                previous.ComposeCandidateId);

    private async Task<List<string>> RemovePreviousRuntimeAsync(
        Guid appId,
        Guid targetId,
        ActiveContainer? old,
        RunnerRuntimeDeploymentMetadata? previous,
        RunnerRuntimeDeploymentMetadata candidate)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(old?.ContainerId)) return warnings;
        var oldRemoved = previous?.ComposeCandidateId is Guid composeCandidateId
            ? await TryRemoveComposeProjectAsync(composeCandidateId, appId, targetId, warnings, "previous runtime")
            : await TryStopOwnedContainerAsync(old.ContainerId, appId, targetId, warnings, "previous runtime");
        if (oldRemoved && previous?.ArtifactRoot is not null
            && !string.Equals(previous.ArtifactRoot, candidate.ArtifactRoot, StringComparison.OrdinalIgnoreCase))
            RunnerWorkspaceService.TryDeletePreviousRuntimeArtifact(previous.ArtifactRoot, warnings);
        return warnings;
    }

    private async Task CompactReadyMetadataAsync(
        RunnerRuntimeDeploymentMetadata readyCandidate,
        CancellationToken cancellationToken,
        ICollection<string> warnings)
    {
        try { await metadataStore.WriteAsync(readyCandidate, cancellationToken); }
        catch (Exception ex)
        {
            warnings.Add($"Runtime metadata compaction deferred until Runner reconciliation: {ex.Message}");
        }
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
        if (!RunnerRuntimeMetadataPolicy.HasExpectedIdentity(identity, appId, targetId))
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

    private async Task<bool> TryRemoveComposeProjectAsync(
        Guid? candidateId,
        Guid appId,
        Guid targetId,
        ICollection<string> warnings,
        string description)
    {
        if (candidateId is null) return true;
        try
        {
            await containers.RemoveComposeProjectAsync(new(appId, targetId), candidateId.Value, CancellationToken.None);
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
                    if (!await SuperviseRuntimeAsync(runtime, cancellationToken)) return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<bool> SuperviseRuntimeAsync(ActiveContainer runtime, CancellationToken cancellationToken)
    {
        DockerContainerIdentity identity;
        try
        {
            identity = await InspectAsync(runtime.AppId, runtime.TargetId, runtime.ContainerId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not inspect managed runtime container {ContainerId}.", runtime.ContainerId);
            return true;
        }

        if (identity.Exists && identity.Running) return true;
        if (!RemoveCurrentRuntime(runtime)) return true;
        await PersistExitedRuntimeAsync(runtime);
        return true;
    }

    private bool RemoveCurrentRuntime(ActiveContainer runtime)
    {
        var key = new DeploymentRuntimeKey(runtime.AppId, runtime.TargetId);
        lock (gate)
        {
            if (!active.TryGetValue(key, out var current)
                || !string.Equals(current.ContainerId, runtime.ContainerId, StringComparison.Ordinal))
                return false;
            active.Remove(key);
        }
        registry.Remove(runtime.AppId, runtime.TargetId);
        return true;
    }

    private async Task PersistExitedRuntimeAsync(ActiveContainer runtime)
    {
        try
        {
            var path = metadataStore.GetPath(runtime.AppId, runtime.TargetId);
            var metadata = await metadataStore.ReadAsync(path, CancellationToken.None);
            if (metadata is null || !string.Equals(metadata.ContainerId, runtime.ContainerId, StringComparison.Ordinal)) return;
            metadata.State = RuntimeReadinessState.Failed;
            metadata.EffectivePort = null;
            metadata.LastError = "The managed runtime container exited unexpectedly.";
            await metadataStore.WriteAsync(metadata, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not persist failed state for exited runtime {ContainerId}.", runtime.ContainerId);
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

    private void Register(Guid appId, Guid targetId, string id, string? name, int? port, Guid? composeCandidateId = null)
    {
        if (port is not > 0) return;
        lock (gate) active[new DeploymentRuntimeKey(appId, targetId)] = new ActiveContainer(appId, targetId, id, name ?? id, port.Value, composeCandidateId);
        registry.Register(appId, targetId, port.Value);
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

    private sealed record ActiveContainer(
        Guid AppId, Guid TargetId, string ContainerId, string ContainerName, int Port, Guid? ComposeCandidateId = null);
}
