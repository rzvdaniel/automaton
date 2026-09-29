using Automaton.Core.Models;
using Automaton.Runner.Console.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>Adapts the application container runtime to the typed host relay.</summary>
public sealed class BrokeredApplicationContainerRuntime : IApplicationContainerRuntime, IStagedApplicationContainerRuntime, IScopedApplicationContainerRuntime
{
    private readonly Func<HostContainerExecutionRequest, CancellationToken, Task<HostContainerExecutionResponse>> execute;
    private readonly Func<Task<RunnerInstallationProfile?>> loadProfile;

    public BrokeredApplicationContainerRuntime(HubService hub, FileStorageService storage)
        : this(hub.RequestHostContainerExecutionAsync, storage.LoadInstallationProfileAsync)
    {
    }

    internal BrokeredApplicationContainerRuntime(
        Func<HostContainerExecutionRequest, CancellationToken, Task<HostContainerExecutionResponse>> execute,
        Func<Task<RunnerInstallationProfile?>> loadProfile)
        => (this.execute, this.loadProfile) = (execute, loadProfile);

    public async Task<DockerImageIdentity> ResolveImageAsync(string image, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Brokered image resolution requires deployment context.");
    }

    public async Task RunBuildAsync(DockerBuildRequest request, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken = default)
    {
        var context = request.Context ?? throw new InvalidOperationException("Brokered builds require deployment execution context.");
        var result = await execute(new()
        {
            Scope = await ScopeAsync(context.AppId, context.DeploymentTargetId, context.ExecutionAttemptId, cancellationToken),
            Operation = HostContainerOperationKinds.RunBuild, RequestedImage = request.Image, Command = request.Command,
            TimeoutSeconds = request.TimeoutSeconds, Environment = environment
        }, cancellationToken);
        EnsureSuccess(result);
    }

    public async Task<DockerApplicationContainer> CreateApplicationAsync(DockerApplicationContainerRequest request, CancellationToken cancellationToken = default)
    {
        if (request.CandidateId == Guid.Empty)
            throw new InvalidOperationException("Brokered application creation requires a candidate identity.");
        if (request.Storage is null)
            throw new InvalidOperationException("Brokered application creation requires the declared storage semantics.");
        var scope = await ScopeAsync(request.AppId, request.DeploymentTargetId, Guid.Empty, cancellationToken);
        var result = await execute(new()
        {
            Scope = scope, Operation = HostContainerOperationKinds.CreateApplicationContainer,
            CandidateId = request.CandidateId, RequestedImage = request.Image, ContainerPort = request.ContainerPort,
            Command = request.Command, Storage = request.Storage, User = request.User
        }, cancellationToken);
        EnsureSuccess(result);
        return new DockerApplicationContainer(result.ContainerId, result.ContainerName, new(request.Image, result.ResolvedImage));
    }

    public async Task StartApplicationAsync(string containerIdOrName, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Brokered existing-container operations require application scope.");

    public async Task<DockerApplicationContainer> StartApplicationAsync(DockerApplicationContainerRequest request, CancellationToken cancellationToken = default)
    {
        var created = await CreateApplicationAsync(request, cancellationToken);
        await StartApplicationAsync(new(request.AppId, request.DeploymentTargetId), created.Id, cancellationToken);
        return created;
    }

    public async Task StopAndRemoveAsync(string containerIdOrName, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Brokered existing-container operations require application scope.");

    public async Task<bool> IsRunningAsync(string containerIdOrName, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Brokered existing-container operations require application scope.");

    public async Task<DockerContainerIdentity> InspectAsync(string containerIdOrName, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Brokered existing-container operations require application scope.");
    }

    public async Task<string> GetLogsAsync(string containerIdOrName, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Brokered existing-container operations require application scope.");
    }

    public async Task<ApplicationContainerStartResult> StartApplicationAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteExistingAsync(scope, HostContainerOperationKinds.StartContainer, id, cancellationToken);
        EnsureSuccess(result);
        return new(result.Running, result.HostAddress!, result.HostPort!.Value, result.ContainerPort!.Value);
    }

    public async Task<DockerContainerIdentity> InspectAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteExistingAsync(scope, HostContainerOperationKinds.InspectContainer, id, cancellationToken);
        EnsureSuccess(result);
        return new(result.ContainerId, result.Exists, result.Running, result.Labels, result.HostAddress, result.HostPort, result.ContainerPort);
    }

    public async Task<bool> IsRunningAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteExistingAsync(scope, HostContainerOperationKinds.InspectContainer, id, cancellationToken);
        EnsureSuccess(result);
        return result.Running;
    }

    public async Task<string> GetLogsAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteExistingAsync(scope, HostContainerOperationKinds.GetContainerLogs, id, cancellationToken);
        EnsureSuccess(result); return result.Logs;
    }

    public async Task StopAndRemoveAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
        => EnsureSuccess(await ExecuteExistingAsync(scope, HostContainerOperationKinds.RemoveContainer, id, cancellationToken));

    private async Task<HostContainerExecutionResponse> ExecuteExistingAsync(ApplicationContainerScope application, string operation, string id, CancellationToken cancellationToken)
    {
        var scope = await ScopeAsync(application.AppId, application.DeploymentTargetId, Guid.Empty, cancellationToken);
        return await execute(new() { Scope = scope, Operation = operation, ContainerId = id }, cancellationToken);
    }

    private async Task<HostContainerScope> ScopeAsync(Guid appId, Guid targetId, Guid attemptId, CancellationToken cancellationToken)
    {
        var profile = await loadProfile()
            ?? throw new InvalidOperationException("Runner installation profile is required for brokered runtime operations.");
        if (profile.RunnerHostId is not Guid hostId || hostId == Guid.Empty)
            throw new InvalidOperationException("Runner Host identity is required for brokered runtime operations.");
        var hosted = Environment.GetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID");
        if (!Guid.TryParse(hosted, out var hostedId))
            throw new InvalidOperationException("Hosted Runner identity is required for brokered runtime operations.");
        return new(hostedId, hostId, appId, targetId, attemptId);
    }

    private static void EnsureSuccess(HostContainerExecutionResponse result)
    {
        if (!result.Success) throw new InvalidOperationException($"Brokered host-container operation failed ({result.ErrorCode}): {result.Error}");
    }
}
