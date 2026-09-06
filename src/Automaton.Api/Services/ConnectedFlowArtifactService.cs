using System.Text.Json;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR;
using CoreFlow = Automaton.Core.Models.Flow;

namespace Automaton.Api.Services;

/// <summary>
/// Synchronizes connected-flow data as managed application artifacts.
/// Flow artifacts are a product resource, not controller source or Action code.
/// </summary>
public sealed class ConnectedFlowArtifactService
{
    private const string FlowIndexFileName = "index.json";
    private readonly AppFileStorageService fileStorageService;
    private readonly UserImpersonationServiceFactory<FlowsService> flowsServiceFactory;
    private readonly IHubContext<AutomatonHub> automatonHub;

    public ConnectedFlowArtifactService(
        AppFileStorageService fileStorageService,
        UserImpersonationServiceFactory<FlowsService> flowsServiceFactory,
        IHubContext<AutomatonHub> automatonHub)
    {
        this.fileStorageService = fileStorageService;
        this.flowsServiceFactory = flowsServiceFactory;
        this.automatonHub = automatonHub;
    }

    public async Task SynchronizeAsync(
        Guid appId,
        Guid deploymentTargetId,
        Guid ownerId,
        IEnumerable<Runner> runners,
        CancellationToken cancellationToken)
    {
        var metadata = await fileStorageService.GetAppStorageMetadataAsync(ownerId, appId);
        var flowIds = metadata.ConnectedFlowIds?
            .Select(value => Guid.TryParse(value, out var parsed) ? parsed : (Guid?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToList() ?? [];

        var flows = new List<CoreFlow>();
        if (flowIds.Count > 0)
        {
            var ownerFlowsService = flowsServiceFactory.CreateForUser(ownerId);
            foreach (var flowId in flowIds)
            {
                var flow = ownerFlowsService.Get(flowId);
                if (flow == null)
                    continue;

                flows.Add(flow);
            }
        }

        var files = BuildArtifactFiles(flows);

        foreach (var runner in runners)
        {
            if (string.IsNullOrWhiteSpace(runner.ConnectionId))
                throw new InvalidOperationException($"Runner '{runner.Name}' is not connected.");

            var result = await automatonHub.Clients.Client(runner.ConnectionId)
                .InvokeAsync<SynchronizeApplicationManagedArtifactsResult>(
                    AutomatonHubMethods.SynchronizeApplicationManagedArtifacts,
                    new SynchronizeApplicationManagedArtifactsRequest(appId, deploymentTargetId, files),
                    cancellationToken);

            ValidateSynchronizationResponse(appId, deploymentTargetId, result);
            if (!result.Success)
                throw new InvalidOperationException(result.Error ?? $"Runner '{runner.Name}' rejected managed artifacts.");
        }
    }

    internal static void ValidateSynchronizationResponse(
        Guid expectedAppId,
        Guid expectedDeploymentTargetId,
        SynchronizeApplicationManagedArtifactsResult result)
    {
        if (result.AppId != expectedAppId)
            throw new InvalidOperationException(
                $"Runner returned managed-artifact response for unexpected app '{result.AppId}'.");

        if (result.DeploymentTargetId != expectedDeploymentTargetId)
            throw new InvalidOperationException(
                $"Runner returned managed-artifact response for unexpected deployment target '{result.DeploymentTargetId}'.");
    }

    internal static IReadOnlyList<ApplicationManagedArtifactFile> BuildArtifactFiles(
        IEnumerable<CoreFlow>? flows)
    {
        var flowList = (flows ?? []).ToList();
        var files = flowList
            .Select(flow => new ApplicationManagedArtifactFile(
                $"{flow.Id:N}.json",
                JsonSerializer.SerializeToUtf8Bytes(flow)))
            .ToList();

        files.Add(new ApplicationManagedArtifactFile(
            FlowIndexFileName,
            JsonSerializer.SerializeToUtf8Bytes(
                flowList.Select(flow => new FlowArtifactIndexEntry(flow.Id, flow.Name)))));

        return files;
    }

    private sealed record FlowArtifactIndexEntry(Guid FlowId, string Name);
}
