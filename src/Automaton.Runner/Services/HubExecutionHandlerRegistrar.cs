using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using static System.Console;

namespace Automaton.Runner.Console.Services;

internal sealed class HubExecutionHandlerRegistrar
{
    private const string RunWorkflowMethod = "RunWorkflow";
    private const string RunCustomStepMethod = "RunCustomStep";
    private const string SynchronizeApplicationManagedArtifactsMethod = "SynchronizeApplicationManagedArtifacts";
    private const string TunnelRequestMethod = "TunnelRequest";
    private const string TunnelResponseMethod = "TunnelResponse";
    private readonly ConfigurationService configService;
    private readonly FlowExecutionService flowExecutionService;
    private readonly TunnelForwarderService tunnelForwarder;
    private readonly IServiceProvider serviceProvider;
    private readonly RunnerWorkspaceService runnerWorkspaceService;
    private readonly ILogger logger;

    public HubExecutionHandlerRegistrar(
        ConfigurationService configService,
        FlowExecutionService flowExecutionService,
        TunnelForwarderService tunnelForwarder,
        IServiceProvider serviceProvider,
        RunnerWorkspaceService runnerWorkspaceService,
        ILogger logger)
    {
        this.configService = configService;
        this.flowExecutionService = flowExecutionService;
        this.tunnelForwarder = tunnelForwarder;
        this.serviceProvider = serviceProvider;
        this.runnerWorkspaceService = runnerWorkspaceService;
        this.logger = logger;
    }

    public void Register(HubConnection connection, Action beginActivity, Action endActivity)
    {
        connection.On<Guid, WorkflowExecution>(RunWorkflowMethod, workflowId =>
            ExecuteWorkflowHandler(workflowId, beginActivity, endActivity));
        connection.On<CustomStepPayload, CustomStepExecutionResponse>(RunCustomStepMethod, customStep =>
            ExecuteCustomStepHandler(customStep, beginActivity, endActivity));
        connection.On<SynchronizeApplicationManagedArtifactsRequest, SynchronizeApplicationManagedArtifactsResult>(
            SynchronizeApplicationManagedArtifactsMethod,
            request => SynchronizeApplicationManagedArtifacts(request));
        connection.On<TunnelRequest>(TunnelRequestMethod, request =>
            ForwardTunnelRequestAsync(connection, request, beginActivity, endActivity));
    }

    private async Task<WorkflowExecution> ExecuteWorkflowHandler(
        Guid workflowId,
        Action beginActivity,
        Action endActivity)
    {
        beginActivity();
        try
        {
            WriteLine($"▶️ Executing workflow: {workflowId}");
            var result = await flowExecutionService.ExecuteWorkflowAsync(workflowId);
            WriteLine($"✅ Workflow completed: {result.Status}");
            return result;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error executing workflow {WorkflowId}", workflowId);
            WriteLine($"❌ Workflow failed: {ex.Message}");
            var failed = new WorkflowExecution(workflowId, configService.ApplicationName)
            {
                Status = WorkflowStatus.Error
            };
            failed.AddLog("Error", ex.Message);
            return failed;
        }
        finally
        {
            endActivity();
        }
    }

    private async Task<CustomStepExecutionResponse> ExecuteCustomStepHandler(
        CustomStepPayload customStep,
        Action beginActivity,
        Action endActivity)
    {
        beginActivity();
        try
        {
            using var scope = serviceProvider.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<CustomStepExecuteService>();
            var result = await handler.RunCustomStep(customStep);
            if (result.Success)
                logger.Debug("Custom step {CustomStepId} completed successfully", customStep.Id);
            else
                logger.Warning("Custom step {CustomStepId} failed: {Error}", customStep.Id, result.Error);
            return result;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error executing custom step {CustomStepId}", customStep.Id);
            WriteLine($"❌ Custom step failed: {ex.Message}");
            return new CustomStepExecutionResponse
            {
                Success = false,
                Output = null,
                Error = $"Handler exception: {ex.Message}",
                Started = DateTime.UtcNow,
                Finished = DateTime.UtcNow,
                OutputVariables = []
            };
        }
        finally
        {
            endActivity();
        }
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
