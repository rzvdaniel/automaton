using Automaton.Api.Data;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Stages bounded Runner artifacts and waits for the persisted command result.</summary>
public interface IRunnerFileTransferGateway
{
    Task<(Guid OperationId, RunnerExecutionResult Result)> ExecuteAsync(Guid operationId, Guid runnerId, Guid ownerId,
        RunnerFileTransferCommand command, IFormFile? input, CancellationToken token, TimeSpan? timeout = null);
    Stream OpenOutput(Guid operationId);
    Task CleanupOutputAsync(Guid operationId);
}

public sealed class RunnerFileTransferGateway(
    ApplicationDbContext db,
    DeploymentPackageStore store,
    RunnerExecutionOperationService operations,
    ResourceAuthorizationService authorization) : IRunnerFileTransferGateway
{
    public async Task<(Guid OperationId, RunnerExecutionResult Result)> ExecuteAsync(
        Guid operationId, Guid runnerId, Guid ownerId, RunnerFileTransferCommand command,
        IFormFile? input, CancellationToken token, TimeSpan? timeout = null)
    {
        string? inputPath = null;
        var reachedTerminalResult = false;
        try
        {
            await EnsureOwnerCanDeployThroughRunnerAsync(runnerId, ownerId, command.AppId, command.DeploymentTargetId, token);
            if (input is not null)
            {
                await using var stream = input.OpenReadStream();
                inputPath = await store.StoreRunnerTransferAsync(operationId, input: true, stream, input.Length, token);
                var sha256 = await store.ComputeSha256Async(inputPath, token);
                command = command with { Length = input.Length, Sha256 = sha256 };
            }

            await EnsureOwnerCanDeployThroughRunnerAsync(runnerId, ownerId, command.AppId, command.DeploymentTargetId, token);
            await operations.QueueFileTransferAsync(operationId, runnerId, ownerId, command, token);
            using var deadline = timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(token)
                : null;
            if (deadline is not null) deadline.CancelAfter(timeout!.Value);
            var result = await operations.WaitAsync(operationId, deadline?.Token ?? token);
            reachedTerminalResult = true;
            return (operationId, result);
        }
        finally
        {
            if (reachedTerminalResult && inputPath is not null)
                await store.TryDeleteRunnerTransferAsync(operationId, input: true);
        }
    }

    public Stream OpenOutput(Guid operationId) => store.OpenRunnerTransfer(operationId, input: false);

    public async Task CleanupOutputAsync(Guid operationId)
    {
        try
        {
            var isTerminal = await db.RunnerExecutionOperations.AsNoTracking()
                .AnyAsync(operation => operation.Id == operationId && operation.CompletedAtUtc != null);
            if (isTerminal)
                await store.TryDeleteRunnerTransferAsync(operationId, input: false);
        }
        catch
        {
            // Retention cleanup reclaims artifacts if the operation state cannot be checked here.
        }
    }

    private async Task EnsureOwnerCanDeployThroughRunnerAsync(
        Guid runnerId, Guid ownerId, Guid appId, Guid? deploymentTargetId, CancellationToken token)
    {
        var targetAssigned = await db.AppDeploymentTargets.AsNoTracking()
            .AnyAsync(item => item.WebAppId == appId
                && item.ExecutionRunnerId == runnerId
                && (!deploymentTargetId.HasValue || item.Id == deploymentTargetId.Value), token);
        if (!targetAssigned || !await authorization.CanUpdateAppAsync(appId, ownerId, token))
            throw new UnauthorizedAccessException("The Runner is not assigned to an application this user can deploy.");

        var runnerProfile = await db.Runners.AsNoTracking()
            .Where(runner => runner.Id == runnerId && runner.Purpose == RunnerPurposes.Managed)
            .Select(runner => runner.ApplicationProfile)
            .SingleOrDefaultAsync(token);
        var isMachineRunner = RunnerApplicationProfiles.Normalize(runnerProfile)
            == RunnerApplicationProfiles.MachineManagement;
        if (!isMachineRunner) return;

        var projectId = await db.WebApps.AsNoTracking()
            .Where(app => app.Id == appId)
            .Select(app => app.ProjectId)
            .SingleOrDefaultAsync(token);
        var workspaceAssigned = projectId is Guid assignedProjectId
            ? await db.ProjectRunners.AnyAsync(link => link.ProjectId == assignedProjectId && link.RunnerId == runnerId, token)
            : await db.WebAppUsers.AnyAsync(link => link.WebAppId == appId && link.UserId == ownerId, token)
              && await db.RunnerUsers.AnyAsync(link => link.RunnerId == runnerId && link.UserId == ownerId, token);
        if (!workspaceAssigned)
            throw new UnauthorizedAccessException("The workspace is not assigned to this Machine Management Runner.");
    }
}
