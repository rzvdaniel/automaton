using Automaton.Api.Data;
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
}

public sealed class RunnerFileTransferGateway(
    ApplicationDbContext db,
    DeploymentPackageStore store,
    RunnerExecutionOperationService operations) : IRunnerFileTransferGateway
{
    public async Task<(Guid OperationId, RunnerExecutionResult Result)> ExecuteAsync(
        Guid operationId, Guid runnerId, Guid ownerId, RunnerFileTransferCommand command,
        IFormFile? input, CancellationToken token, TimeSpan? timeout = null)
    {
        string? inputPath = null;
        var reachedTerminalResult = false;
        try
        {
            await EnsureOwnerStillOwnsRunnerAsync(runnerId, ownerId, command.AppId, command.DeploymentTargetId, token);
            if (input is not null)
            {
                await using var stream = input.OpenReadStream();
                inputPath = await store.StoreRunnerTransferAsync(operationId, input: true, stream, input.Length, token);
                var sha256 = await store.ComputeSha256Async(inputPath, token);
                command = command with { Length = input.Length, Sha256 = sha256 };
            }

            await EnsureOwnerStillOwnsRunnerAsync(runnerId, ownerId, command.AppId, command.DeploymentTargetId, token);
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
                store.TryDeleteRunnerTransfer(operationId, input: true);
        }
    }

    public Stream OpenOutput(Guid operationId) => store.OpenRunnerTransfer(operationId, input: false);

    private async Task EnsureOwnerStillOwnsRunnerAsync(
        Guid runnerId, Guid ownerId, Guid appId, Guid? deploymentTargetId, CancellationToken token)
    {
        var ownsAssignment = await db.AppDeploymentTargets.AsNoTracking()
            .AnyAsync(target => target.WebAppId == appId && target.ExecutionRunnerId == runnerId
                && (!deploymentTargetId.HasValue || target.Id == deploymentTargetId.Value)
                && target.WebApp!.WebAppUsers.Any(user => user.UserId == ownerId), token);
        if (!ownsAssignment)
            throw new UnauthorizedAccessException("The Runner is not assigned to an application owned by this account.");
    }
}
