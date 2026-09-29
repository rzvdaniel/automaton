using System.Collections.Concurrent;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

public class RunnerDeploymentService
{
    private readonly RunnerWorkspaceService runnerWorkspaceService;
    private readonly HostedRunnerDeploymentResultJournal resultJournal;
    private readonly IAppRuntimeProcessManager runtimeProcessManager;
    private readonly RunnerApplicationDeploymentExecutor deploymentExecutor;
    private readonly ConcurrentDictionary<(Guid AppId, Guid TargetId), Guid> activeExecutionAttempts = new();
    private readonly ConcurrentDictionary<(Guid AppId, Guid TargetId), CancellationTokenSource> executionCancellations = new();
    private readonly ConcurrentDictionary<(Guid AppId, Guid TargetId), SemaphoreSlim> deploymentGates = new();

    public RunnerDeploymentService(
        RunnerWorkspaceService runnerWorkspaceService,
        IAppRuntimeProcessManager runtimeProcessManager,
        AppActionDeploymentService actionDeploymentService,
        IRunnerApplicationBuildExecutor? buildExecutor = null)
    {
        this.runnerWorkspaceService = runnerWorkspaceService;
        resultJournal = new HostedRunnerDeploymentResultJournal(runnerWorkspaceService);
        this.runtimeProcessManager = runtimeProcessManager;
        var resolvedBuildExecutor = buildExecutor ?? new RunnerApplicationBuildExecutor(new DockerContainerRuntime());
        deploymentExecutor = new RunnerApplicationDeploymentExecutor(
            runnerWorkspaceService, runtimeProcessManager, actionDeploymentService, resolvedBuildExecutor, resultJournal);
    }

    public async Task<DeployAppPackageResult> DeployPackage(DeployAppPackageRequest request)
    {
        var completedResult = await resultJournal.TryReadAsync(request);
        if (completedResult is not null)
            return completedResult;

        if (request.ExecutionAttemptId is not Guid executionAttemptId)
            return await DeployPackageCore(request);

        var key = (request.AppId, request.DeploymentTargetId);
        activeExecutionAttempts[key] = executionAttemptId;
        var executionCancellation = new CancellationTokenSource();
        if (executionCancellations.TryGetValue(key, out var previousCancellation))
            previousCancellation.Cancel();
        executionCancellations[key] = executionCancellation;
        var gate = deploymentGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await DeployPackageCore(request, executionCancellation.Token);
        }
        finally
        {
            executionCancellation.Cancel();
            executionCancellation.Dispose();
            gate.Release();
            if (activeExecutionAttempts.TryGetValue(key, out var activeAttempt)
                && activeAttempt == executionAttemptId)
            {
                ((ICollection<KeyValuePair<(Guid AppId, Guid TargetId), Guid>>)activeExecutionAttempts)
                    .Remove(new KeyValuePair<(Guid AppId, Guid TargetId), Guid>(key, executionAttemptId));
            }
            if (executionCancellations.TryGetValue(key, out var activeCancellation)
                && ReferenceEquals(activeCancellation, executionCancellation))
            {
                ((ICollection<KeyValuePair<(Guid AppId, Guid TargetId), CancellationTokenSource>>)executionCancellations)
                    .Remove(new KeyValuePair<(Guid AppId, Guid TargetId), CancellationTokenSource>(key, executionCancellation));
            }
        }
    }

    public void ForgetHostedRunnerDeploymentResult(Guid operationId, Guid attemptId)
        => resultJournal.Delete(operationId, attemptId);

    private async Task<DeployAppPackageResult> DeployPackageCore(
        DeployAppPackageRequest request,
        CancellationToken executionCancellationToken = default)
        => await deploymentExecutor.DeployAsync(
            request,
            executionCancellationToken,
            () => EnsureExecutionAttemptIsCurrent(request, executionCancellationToken));

    private void EnsureExecutionAttemptIsCurrent(
        DeployAppPackageRequest request,
        CancellationToken executionCancellationToken)
    {
        executionCancellationToken.ThrowIfCancellationRequested();
        if (request.ExecutionAttemptId is Guid attemptId
            && (!activeExecutionAttempts.TryGetValue(
                    (request.AppId, request.DeploymentTargetId),
                    out var activeAttempt)
                || activeAttempt != attemptId))
        {
            throw new InvalidOperationException(
                "This deployment execution attempt was superseded before runtime promotion.");
        }
    }

    public async Task CleanupTargetAsync(
        Guid appId,
        Guid deploymentTargetId)
    {
        await runtimeProcessManager.StopAsync(appId, deploymentTargetId);
        runnerWorkspaceService.DeleteApplicationTarget(appId, deploymentTargetId);
    }
}
