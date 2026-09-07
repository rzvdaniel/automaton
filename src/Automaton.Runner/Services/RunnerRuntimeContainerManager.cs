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
        foreach (var metadataPath in Directory.Exists(workspace.AppsRoot)
            ? Directory.EnumerateFiles(workspace.AppsRoot, MetadataFileName, SearchOption.AllDirectories)
            : [])
        {
            try
            {
                var metadata = await ReadMetadataAsync(metadataPath, cancellationToken);
                if (metadata?.Runtime?.Command is null || string.IsNullOrWhiteSpace(metadata.Runtime.Image))
                    continue;
                if (!Guid.TryParse(metadata.AppId, out var appId)
                    || !Guid.TryParse(metadata.DeploymentTargetId, out var targetId))
                    continue;

                if (!string.IsNullOrWhiteSpace(metadata.ContainerId)
                    && await containers.IsRunningAsync(metadata.ContainerId, cancellationToken))
                {
                    Register(appId, targetId, metadata.ContainerId, metadata.ContainerName, metadata.EffectivePort);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(metadata.ArtifactRoot) || !Directory.Exists(metadata.ArtifactRoot))
                    continue;
                await ApplyAsync(
                    appId,
                    targetId,
                    new ApplicationContract
                    {
                        Runtime = metadata.Runtime,
                        Health = metadata.Health ?? new ApplicationHealthSpec(),
                        Storage = metadata.Storage ?? new ApplicationStorageSpec()
                    },
                    cancellationToken,
                    metadata.ArtifactRoot);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not reconcile managed application container metadata from {MetadataPath}", metadataPath);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => StopAllAsync(cancellationToken);

    public async Task<RuntimeApplyResult> ApplyAsync(
        Guid appId,
        Guid deploymentTargetId,
        ApplicationContract contract,
        CancellationToken cancellationToken = default,
        string? artifactRoot = null)
    {
        ApplicationContractValidator.EnsureValid(contract);
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
        var effectivePort = AllocatePort(contract.Runtime.Port);
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
            await WriteMetadataAsync(new RunnerRuntimeDeploymentMetadata
            {
                AppId = appId.ToString(),
                DeploymentTargetId = deploymentTargetId.ToString(),
                Runtime = contract.Runtime,
                Health = contract.Health,
                Storage = contract.Storage,
                Candidate = candidate,
                EffectivePort = previous?.EffectivePort,
                State = RuntimeReadinessState.Starting
            }, cancellationToken);

            await WaitForReadinessAsync(effectivePort, contract.Health, cancellationToken);
            await PromoteAsync(appId, deploymentTargetId, previous, candidate, cancellationToken);
            return new RuntimeApplyResult { PromotionIrreversible = true };
        }
        catch
        {
            if (started is not null)
            {
                var logs = await containers.GetLogsAsync(started.Id, CancellationToken.None);
                logger.LogWarning(
                    "Managed application container {ContainerId} failed before promotion. Logs: {Logs}",
                    started.Id,
                    logs);
                await containers.StopAndRemoveAsync(started.Id, CancellationToken.None);
            }
            if (previous is not null)
                await WriteMetadataAsync(previous, CancellationToken.None);
            else if (File.Exists(metadataPath))
                File.Delete(metadataPath);
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

    private async Task PromoteAsync(
        Guid appId,
        Guid targetId,
        RunnerRuntimeDeploymentMetadata? previous,
        RunnerRuntimeDeploymentMetadata candidate,
        CancellationToken cancellationToken)
    {
        var key = new DeploymentRuntimeKey(appId, targetId);
        ActiveContainer? old;
        lock (gate)
        {
            active.TryGetValue(key, out old);
            if (old is null && !string.IsNullOrWhiteSpace(previous?.ContainerId))
                old = new ActiveContainer(previous.ContainerId!, previous.ContainerName ?? previous.ContainerId!, previous.EffectivePort ?? 0);
            active[key] = new ActiveContainer(candidate.ContainerId!, candidate.ContainerName!, candidate.EffectivePort!.Value);
            registry.Register(appId, targetId, candidate.EffectivePort.Value);
        }

        if (!string.IsNullOrWhiteSpace(old?.ContainerId))
            await containers.StopAndRemoveAsync(old.ContainerId, CancellationToken.None);

        candidate.State = RuntimeReadinessState.Ready;
        candidate.Candidate = null;
        await WriteMetadataAsync(candidate, cancellationToken);
    }

    private async Task StopAllAsync(CancellationToken cancellationToken)
    {
        ActiveContainer[] current;
        lock (gate)
        {
            current = active.Values.ToArray();
            active.Clear();
        }
        foreach (var runtime in current)
            await containers.StopAndRemoveAsync(runtime.ContainerId, cancellationToken);
    }

    private async Task WaitForReadinessAsync(int port, ApplicationHealthSpec health, CancellationToken cancellationToken)
    {
        var healthPort = health.Port ?? port;
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
            await Task.Delay(100, timeout.Token);
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

    private void Register(Guid appId, Guid targetId, string id, string? name, int? port)
    {
        if (port is not > 0) return;
        lock (gate) active[new DeploymentRuntimeKey(appId, targetId)] = new ActiveContainer(id, name ?? id, port.Value);
        registry.Register(appId, targetId, port.Value);
    }

    private int AllocatePort(int requested)
    {
        if (requested is >= 1 and <= 65535 && !registry.IsPortRegistered(requested))
            return requested;
        for (var port = portStart; port <= portEnd; port++)
        {
            if (!registry.IsPortRegistered(port)) return port;
        }
        throw new InvalidOperationException("No managed runtime ports are available.");
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

    private sealed record ActiveContainer(string ContainerId, string ContainerName, int Port);
}
