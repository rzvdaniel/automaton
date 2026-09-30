using Automaton.Core.Models;
using Automaton.Runner.Console.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;
using static System.Console;

namespace Automaton.Runner.Console.Services;

internal sealed class HubExecutionHandlerRegistrar
{
    private const string SynchronizeApplicationManagedArtifactsMethod = "SynchronizeApplicationManagedArtifacts";
    private const string TunnelRequestMethod = "TunnelRequest";
    private const string TunnelResponseMethod = "TunnelResponse";
    private readonly TunnelForwarderService tunnelForwarder;
    private readonly RunnerWorkspaceService runnerWorkspaceService;
    private readonly ILogger logger;

    public HubExecutionHandlerRegistrar(
        TunnelForwarderService tunnelForwarder,
        RunnerWorkspaceService runnerWorkspaceService,
        ILogger logger)
    {
        this.tunnelForwarder = tunnelForwarder;
        this.runnerWorkspaceService = runnerWorkspaceService;
        this.logger = logger;
    }

    public void Register(HubConnection connection, Action beginActivity, Action endActivity)
    {
        connection.On<SynchronizeApplicationManagedArtifactsRequest, SynchronizeApplicationManagedArtifactsResult>(
            SynchronizeApplicationManagedArtifactsMethod,
            request => SynchronizeApplicationManagedArtifacts(request));
        connection.On<TunnelRequest>(TunnelRequestMethod, request =>
            ForwardTunnelRequestAsync(connection, request, beginActivity, endActivity));
    }

    private SynchronizeApplicationManagedArtifactsResult SynchronizeApplicationManagedArtifacts(
        SynchronizeApplicationManagedArtifactsRequest request)
    {
        var artifactRoot = runnerWorkspaceService.GetAppFlowArtifactsRoot(request.AppId, request.DeploymentTargetId);
        try
        {
            ManagedApplicationArtifactSynchronizer.Replace(
                artifactRoot,
                request.Files ?? [],
                runnerWorkspaceService.NormalizeRelativePath);
            return new SynchronizeApplicationManagedArtifactsResult(request.AppId, request.DeploymentTargetId, true);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to synchronize managed flow artifacts for AppId={AppId}", request.AppId);
            return new SynchronizeApplicationManagedArtifactsResult(
                request.AppId, request.DeploymentTargetId, false, ex.Message);
        }
    }

    private async Task ForwardTunnelRequestAsync(
        HubConnection connection,
        TunnelRequest request,
        Action beginActivity,
        Action endActivity)
    {
        beginActivity();
        logger.Debug("Tunnel request received: {Method} {Path} RequestId={RequestId}",
            request.Method, request.Path, request.RequestId);
        try
        {
            var response = await tunnelForwarder.ForwardAsync(request);
            if (response.StatusCode >= 200 && response.StatusCode < 300)
                logger.Debug("Tunnel response sent: {StatusCode} RequestId={RequestId}", response.StatusCode, request.RequestId);
            else
            {
                logger.Warning("Tunnel forwarded with non-2xx: {StatusCode} RequestId={RequestId} Error={Error}",
                    response.StatusCode, request.RequestId, response.Error);
                WriteLine($"⚠️ Controller error ({response.StatusCode}): {response.Error ?? "(no details)"}  [{request.Path}]");
            }
            await connection.SendAsync(TunnelResponseMethod, response);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Tunnel forwarding failed: {Method} {Path} RequestId={RequestId}",
                request.Method, request.Path, request.RequestId);
        }
        finally
        {
            endActivity();
        }
    }
}
