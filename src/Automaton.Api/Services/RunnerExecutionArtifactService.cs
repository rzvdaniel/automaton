using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Authorizes bounded binary transfer against the persisted Runner operation contract.</summary>
public sealed class RunnerExecutionArtifactService(
    ApplicationDbContext db,
    RunnerExecutionIdentityService identity,
    DeploymentPackageStore store,
    IDataProtectionProvider protection)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector protector = protection.CreateProtector("RunnerExecutionOperation.v1");

    public async Task<FileStream?> OpenInputAsync(
        Guid keyId, Guid runnerId, Guid actorId, Guid operationId, CancellationToken token)
    {
        var (operation, transfer) = await AuthorizeAsync(keyId, runnerId, actorId, operationId, token);
        if (transfer.Kind is not (RunnerFileTransferKinds.UploadAppFile
            or RunnerFileTransferKinds.UploadMedia or RunnerFileTransferKinds.DeployPackage))
            throw new UnauthorizedAccessException("This operation has no Runner input artifact.");
        EnsureArtifactTransferIsActive(operation);
        return store.OpenRunnerTransfer(operationId, input: true);
    }

    public async Task StoreOutputAsync(
        Guid keyId, Guid runnerId, Guid actorId, Guid operationId, Stream content, long? contentLength,
        CancellationToken token)
    {
        var (operation, transfer) = await AuthorizeAsync(keyId, runnerId, actorId, operationId, token);
        if (transfer.Kind is not (RunnerFileTransferKinds.DownloadAppFile or RunnerFileTransferKinds.DownloadMedia))
            throw new UnauthorizedAccessException("This operation does not accept an output artifact.");
        EnsureArtifactTransferIsActive(operation);
        if (contentLength is null)
            throw new InvalidDataException("A Content-Length is required for Runner output artifacts.");

        await store.StoreRunnerTransferAsync(operationId, input: false, content, contentLength.Value, token);
    }

    private async Task<(RunnerExecutionOperation Operation, RunnerFileTransferCommand Transfer)> AuthorizeAsync(
        Guid keyId, Guid runnerId, Guid actorId, Guid operationId, CancellationToken token)
    {
        var operation = await db.RunnerExecutionOperations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == operationId, token)
            ?? throw new UnauthorizedAccessException("Runner operation was not found.");
        if (operation.RunnerId != runnerId)
            throw new UnauthorizedAccessException("Runner operation belongs to another Runner.");
        await identity.EnsureAsync(keyId, runnerId, actorId, token);
        var request = JsonSerializer.Deserialize<RunnerExecutionRequest>(
            protector.Unprotect(operation.ProtectedRequest), JsonOptions);
        return (operation, request?.FileTransfer
            ?? throw new UnauthorizedAccessException("Runner operation is not a file transfer."));
    }

    private static void EnsureArtifactTransferIsActive(RunnerExecutionOperation operation)
    {
        if (operation.CompletedAtUtc.HasValue || operation.CancellationRequested)
            throw new InvalidOperationException("Runner operation is no longer accepting artifact transfers.");
    }
}
