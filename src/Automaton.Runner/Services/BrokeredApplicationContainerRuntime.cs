using System.Globalization;
using System.Text.RegularExpressions;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>Adapts the application container runtime to the typed host relay.</summary>
public sealed class BrokeredApplicationContainerRuntime : IApplicationContainerRuntime, IStagedApplicationContainerRuntime
{
    private readonly HubService hub;
    private readonly FileStorageService storage;
    private readonly RunnerWorkspaceService workspace;

    public BrokeredApplicationContainerRuntime(HubService hub, FileStorageService storage, RunnerWorkspaceService workspace)
        => (this.hub, this.storage, this.workspace) = (hub, storage, workspace);

    public async Task<DockerImageIdentity> ResolveImageAsync(string image, CancellationToken cancellationToken = default)
    {
        var scope = await ScopeAsync(Guid.Empty, Guid.Empty, Guid.Empty, cancellationToken);
        var result = await hub.RequestHostContainerExecutionAsync(new() { Scope = scope, Operation = HostContainerOperationKinds.ResolveImage, RequestedImage = image }, cancellationToken);
        EnsureSuccess(result);
        return new DockerImageIdentity(image, result.ResolvedImage);
    }

    public async Task RunBuildAsync(DockerBuildRequest request, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Brokered builds require deployment execution context and are wired by RunnerDeploymentService.");

    public async Task<DockerApplicationContainer> CreateApplicationAsync(DockerApplicationContainerRequest request, CancellationToken cancellationToken = default)
    {
        var candidate = ParseCandidateId(request.ArtifactRoot);
        var scope = await ScopeAsync(request.AppId, request.DeploymentTargetId, Guid.Empty, cancellationToken);
        var result = await hub.RequestHostContainerExecutionAsync(new()
        {
            Scope = scope, Operation = HostContainerOperationKinds.CreateApplicationContainer,
            CandidateId = candidate, RequestedImage = request.Image, ContainerPort = request.ContainerPort,
            Command = request.Command, Storage = new ApplicationStorageSpec { DataDirectory = request.DataDirectory },
            Resources = new(HostContainerResourceKinds.Source, HostContainerResourceKinds.Artifact, HostContainerResourceKinds.ApplicationData)
        }, cancellationToken);
        EnsureSuccess(result);
        return new DockerApplicationContainer(result.ContainerId, result.ContainerName, new(request.Image, result.ResolvedImage));
    }

    public async Task StartApplicationAsync(string containerIdOrName, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteExistingAsync(HostContainerOperationKinds.StartContainer, containerIdOrName, cancellationToken);
        EnsureSuccess(result);
    }

    public async Task<DockerApplicationContainer> StartApplicationAsync(DockerApplicationContainerRequest request, CancellationToken cancellationToken = default)
    {
        var created = await CreateApplicationAsync(request, cancellationToken);
        await StartApplicationAsync(created.Id, cancellationToken);
        return created;
    }

    public async Task StopAndRemoveAsync(string containerIdOrName, CancellationToken cancellationToken = default)
        => EnsureSuccess(await ExecuteExistingAsync(HostContainerOperationKinds.RemoveContainer, containerIdOrName, cancellationToken));

    public async Task<bool> IsRunningAsync(string containerIdOrName, CancellationToken cancellationToken = default)
        => (await ExecuteExistingAsync(HostContainerOperationKinds.InspectContainer, containerIdOrName, cancellationToken)).Running;

    public async Task<DockerContainerIdentity> InspectAsync(string containerIdOrName, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteExistingAsync(HostContainerOperationKinds.InspectContainer, containerIdOrName, cancellationToken);
        EnsureSuccess(result);
        return new(result.ContainerId, result.Exists, result.Running, result.Labels);
    }

    public async Task<string> GetLogsAsync(string containerIdOrName, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteExistingAsync(HostContainerOperationKinds.GetContainerLogs, containerIdOrName, cancellationToken);
        EnsureSuccess(result);
        return result.Logs;
    }

    private async Task<HostContainerExecutionResponse> ExecuteExistingAsync(string operation, string id, CancellationToken cancellationToken)
    {
        var scope = await ScopeAsync(Guid.Empty, Guid.Empty, Guid.Empty, cancellationToken);
        return await hub.RequestHostContainerExecutionAsync(new() { Scope = scope, Operation = operation, ContainerId = id }, cancellationToken);
    }

    private async Task<HostContainerScope> ScopeAsync(Guid appId, Guid targetId, Guid attemptId, CancellationToken cancellationToken)
    {
        var profile = await storage.LoadInstallationProfileAsync()
            ?? throw new InvalidOperationException("Runner installation profile is required for brokered runtime operations.");
        if (profile.RunnerHostId is not Guid hostId || hostId == Guid.Empty)
            throw new InvalidOperationException("Runner Host identity is required for brokered runtime operations.");
        var hosted = Environment.GetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID");
        if (!Guid.TryParse(hosted, out var hostedId))
            throw new InvalidOperationException("Hosted Runner identity is required for brokered runtime operations.");
        return new(hostedId, hostId, appId, targetId, attemptId);
    }

    private static Guid ParseCandidateId(string artifactRoot)
    {
        var match = Regex.Match(artifactRoot.Replace('\\', '/'), @"/candidates/(?<id>[0-9a-fA-F-]{36})/application\z", RegexOptions.CultureInvariant);
        return match.Success && Guid.TryParse(match.Groups["id"].Value, out var candidate)
            ? candidate : throw new InvalidOperationException("Brokered runtime requires a candidate artifact identity.");
    }

    private static void EnsureSuccess(HostContainerExecutionResponse result)
    {
        if (!result.Success) throw new InvalidOperationException($"Brokered host-container operation failed ({result.ErrorCode}): {result.Error}");
    }
}
