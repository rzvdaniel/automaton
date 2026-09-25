using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Api.Constants;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Transfers a deployment package to its assigned Runner and invokes deployment.
/// </summary>
public sealed class DeploymentPackageRunnerService(
    ApplicationDbContext dataContext,
    IHubContext<AutomatonHub> automatonHub,
    FileUploadRegistry uploadRegistry,
    ApiInstanceIdentityService apiInstanceIdentityService)
{
    public async Task<DeployAppPackageResult> DeployAsync(
        Guid appId,
        Guid deploymentTargetId,
        Runner runner,
        IFormFile file,
        ApplicationContract packageApplication,
        bool clean,
        Guid? deploymentOperationId,
        Guid? executionAttemptId,
        CancellationToken cancellationToken)
    {
        await EnsureRunnerOwnershipAsync(runner, cancellationToken);
        RunnerProtocolCompatibilityPolicy.EnsureSupportsManagedApplicationArtifacts(runner);
        if (packageApplication.Runtime.Command is not null || packageApplication.Build.Command is not null)
            RunnerProtocolCompatibilityPolicy.EnsureSupportsContainerApplicationExecution(runner);

        var completion = await UploadPackageAsync(appId, runner, file, cancellationToken);
        await EnsureRunnerOwnershipAsync(runner, cancellationToken);
        return await automatonHub.Clients.Client(runner.ConnectionId!)
            .InvokeAsync<DeployAppPackageResult>(
                AutomatonHubMethods.DeployAppPackage,
                new DeployAppPackageRequest
                {
                    AppId = appId,
                    DeploymentTargetId = deploymentTargetId,
                    PackagePath = completion.LocalPath,
                    Clean = clean,
                    DeploymentOperationId = deploymentOperationId,
                    ExecutionAttemptId = executionAttemptId,
                    SystemInfrastructure = InfrastructureAppTrust.IsTrustedApp(appId)
                        && string.Equals(runner.Purpose, RunnerPurposes.Infrastructure, StringComparison.Ordinal)
                },
                CancellationToken.None);
    }

    private async Task EnsureRunnerOwnershipAsync(Runner runner, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runner.ApiInstanceId))
            throw new DeploymentOperationRunnerOwnershipChangedException(runner.Id);

        var current = await dataContext.Runners.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == runner.Id, cancellationToken);
        if (current is null
            || !string.Equals(current.ApiInstanceId, apiInstanceIdentityService.ApiInstanceId, StringComparison.Ordinal)
            || !string.Equals(current.ConnectionId, runner.ConnectionId, StringComparison.Ordinal))
        {
            throw new DeploymentOperationRunnerOwnershipChangedException(runner.Id);
        }
    }

    private async Task<FileUploadCompleteMessage> UploadPackageAsync(
        Guid appId,
        Runner runner,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        const int chunkSize = 256 * 1024;
        var (sessionId, completionTask) = uploadRegistry.Register(TimeSpan.FromMinutes(10), cancellationToken);
        using var stream = file.OpenReadStream();
        var buffer = new byte[chunkSize];
        var chunkIndex = 0;
        try
        {
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await EnsureRunnerOwnershipAsync(runner, cancellationToken);
                var chunk = new FileUploadChunk
                {
                    SessionId = sessionId,
                    ChunkIndex = chunkIndex++,
                    Data = bytesRead == chunkSize ? buffer : buffer[..bytesRead],
                    IsLast = stream.Position >= stream.Length,
                    FileName = $"{appId:N}.zip",
                    ContentType = "application/zip",
                    AppId = appId
                };
                await automatonHub.Clients.Client(runner.ConnectionId!)
                    .SendAsync(AutomatonHubMethods.ReceiveFileChunk, chunk, cancellationToken);
            }
            while (true)
            {
                var completed = await Task.WhenAny(completionTask, Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken));
                if (completed == completionTask)
                    return await completionTask;
                await EnsureRunnerOwnershipAsync(runner, cancellationToken);
            }
        }
        finally
        {
            uploadRegistry.TryCancel(sessionId);
        }
    }
}
