using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class RunnerExecutionOperationService(
    ApplicationDbContext db, IEventPublisher events, ITransactionalEventExecutor transactions,
    IDataProtectionProvider protection, RunnerExecutionIdentityService identity, DeploymentPackageStore artifacts)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector protector = protection.CreateProtector("RunnerExecutionOperation.v1");

    public async Task QueueManagedArtifactsAsync(Guid id, Guid runnerId, Guid ownerId,
        SynchronizeApplicationManagedArtifactsRequest request, CancellationToken token)
    {
        if (await HasManagedArtifactsOperationAsync(id, runnerId, ownerId, request, token)) return;
        try { await QueueAsync(runnerId, ownerId, new(null, null, ManagedArtifacts: request), token, id); }
        catch (DbUpdateException)
        {
            // Competing API callbacks may create the same operation. Its committed, pinned request wins.
            var added = db.ChangeTracker.Entries<RunnerExecutionOperation>()
                .SingleOrDefault(entry => entry.Entity.Id == id && entry.State == EntityState.Added);
            if (added is not null) added.State = EntityState.Detached;
            if (!await HasManagedArtifactsOperationAsync(id, runnerId, ownerId, request, token)) throw;
        }
    }

    public async Task QueueFileTransferAsync(Guid id, Guid runnerId, Guid ownerId,
        RunnerFileTransferCommand request, CancellationToken token)
    {
        var payload = new RunnerExecutionRequest(null, null, FileTransfer: request);
        if (await HasFileTransferOperationAsync(id, runnerId, ownerId, request, token)) return;
        try { await QueueAsync(runnerId, ownerId, payload, token, id); }
        catch (DbUpdateException)
        {
            var added = db.ChangeTracker.Entries<RunnerExecutionOperation>()
                .SingleOrDefault(entry => entry.Entity.Id == id && entry.State == EntityState.Added);
            if (added is not null) added.State = EntityState.Detached;
            if (!await HasFileTransferOperationAsync(id, runnerId, ownerId, request, token)) throw;
        }
    }

    public async Task QueueHostContainerAsync(Guid id, Guid runnerId, Guid ownerId,
        HostContainerExecutionRequest request, CancellationToken token)
    {
        var payload = new RunnerExecutionRequest(null, null, HostContainer: request);
        if (await HasHostContainerOperationAsync(id, runnerId, ownerId, request, token)) return;
        try { await QueueAsync(runnerId, ownerId, payload, token, id); }
        catch (DbUpdateException)
        {
            var added = db.ChangeTracker.Entries<RunnerExecutionOperation>()
                .SingleOrDefault(entry => entry.Entity.Id == id && entry.State == EntityState.Added);
            if (added is not null) added.State = EntityState.Detached;
            if (!await HasHostContainerOperationAsync(id, runnerId, ownerId, request, token)) throw;
        }
    }

    public async Task<HostContainerExecutionOperationResponse?> ReadHostContainerOperationAsync(
        Guid keyId, Guid operationId, Guid authenticatedUserId, Guid runnerId, CancellationToken token)
    {
        var operation = await AuthorizeAsync(keyId, operationId, authenticatedUserId, runnerId, token);
        var request = JsonSerializer.Deserialize<RunnerExecutionRequest>(
            protector.Unprotect(operation.ProtectedRequest), JsonOptions);
        if (request?.HostContainer is null)
            throw new UnauthorizedAccessException("The Runner operation is not a typed container operation.");
        if (operation.ResultJson is null)
            return new(operation.Id, false, null);
        var result = JsonSerializer.Deserialize<RunnerExecutionResult>(protector.Unprotect(operation.ResultJson), JsonOptions)
            ?? throw new InvalidDataException("The saved typed container result is invalid.");
        var hostContainerResult = result.HostContainerResult ?? result.Outcome switch
        {
            "Lost" => new HostContainerExecutionResponse
            {
                ErrorCode = "operation_lost",
                Error = result.Error ?? "The Runner stopped before the typed container operation completed."
            },
            "Cancelled" => new HostContainerExecutionResponse
            {
                ErrorCode = "operation_cancelled",
                Error = result.Error ?? "The typed container operation was cancelled."
            },
            _ => throw new InvalidDataException("The saved Runner operation has no typed container result.")
        };
        return new(operation.Id, true, hostContainerResult);
    }

    private async Task<bool> HasFileTransferOperationAsync(Guid id, Guid runnerId, Guid ownerId,
        RunnerFileTransferCommand request, CancellationToken token)
    {
        var operation = await db.RunnerExecutionOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, token);
        if (operation is null) return false;
        var pinned = JsonSerializer.Deserialize<RunnerExecutionRequest>(protector.Unprotect(operation.ProtectedRequest), JsonOptions);
        if (operation.RunnerId != runnerId || operation.RequestedByUserId != ownerId || pinned?.FileTransfer != request)
            throw new UnauthorizedAccessException("Runner file-transfer operation identity is already assigned to another request.");
        return true;
    }

    private async Task<bool> HasManagedArtifactsOperationAsync(Guid id, Guid runnerId, Guid ownerId,
        SynchronizeApplicationManagedArtifactsRequest request, CancellationToken token)
    {
        var operation = await db.RunnerExecutionOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, token);
        if (operation is null) return false;
        var pinned = JsonSerializer.Deserialize<RunnerExecutionRequest>(protector.Unprotect(operation.ProtectedRequest), JsonOptions);
        if (operation.RunnerId != runnerId || operation.RequestedByUserId != ownerId
            || pinned?.ManagedArtifacts?.AppId != request.AppId
            || pinned.ManagedArtifacts.DeploymentTargetId != request.DeploymentTargetId)
            throw new UnauthorizedAccessException("Managed artifact operation identity belongs to another assignment.");
        return true;
    }

    private async Task<bool> HasHostContainerOperationAsync(Guid id, Guid runnerId, Guid ownerId,
        HostContainerExecutionRequest request, CancellationToken token)
    {
        var operation = await db.RunnerExecutionOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, token);
        if (operation is null) return false;
        var pinned = JsonSerializer.Deserialize<RunnerExecutionRequest>(protector.Unprotect(operation.ProtectedRequest), JsonOptions);
        if (operation.RunnerId != runnerId || operation.RequestedByUserId != ownerId
            || pinned?.HostContainer is null
            || !string.Equals(
                JsonSerializer.Serialize(pinned.HostContainer, JsonOptions),
                JsonSerializer.Serialize(request, JsonOptions),
                StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Typed container operation identity is already assigned to another request.");
        return true;
    }

    public async Task<Guid> QueueAsync(Guid runnerId, Guid actor, RunnerExecutionRequest request, CancellationToken token, Guid? operationId = null)
    {
        var operation = new RunnerExecutionOperation
        {
            Id = operationId ?? Guid.NewGuid(), RunnerId = runnerId, RequestedByUserId = actor,
            ProtectedRequest = protector.Protect(JsonSerializer.Serialize(request, JsonOptions))
        };
        await transactions.ExecuteAsync(async ct =>
        {
            await db.EnsureRunnerOperationAdmissionAsync(runnerId, ct);
            db.RunnerExecutionOperations.Add(operation);
            await db.SaveChangesAsync(ct);
            await events.PublishAsync(new RunnerExecutionRequestedEvent(operation.Id, runnerId,
                IsManagementRequest(request)), ct);
        }, token);
        return operation.Id;
    }

    private static bool IsManagementRequest(RunnerExecutionRequest request)
        => request.ManagedArtifacts is not null
            || request.FileTransfer?.Kind is RunnerFileTransferKinds.ReadMediaStats or RunnerFileTransferKinds.ReadMediaMetadata
            || request.HostContainer is not null
            // Cleanup waits for typed Docker work on the management queue. It must
            // leave that queue's single consumer free to run its nested operations.
            || request.Action is not null && request.Action.Kind is not
                ("execute" or "cleanup-app-resources" or "cleanup-target-resources" or "cleanup-app" or "cleanup-target");

    public async Task<RunnerExecutionResult> WaitAsync(Guid operationId, CancellationToken token)
    {
        try
        {
            while (true)
            {
                var operation = await db.RunnerExecutionOperations.AsNoTracking()
                    .SingleAsync(o => o.Id == operationId, token);
                if (operation.ResultJson is not null)
                    return JsonSerializer.Deserialize<RunnerExecutionResult>(protector.Unprotect(operation.ResultJson), JsonOptions)!;
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await CancelAsync(operationId, CancellationToken.None);
            throw;
        }
    }

    public async Task<bool> CancelAsync(Guid operationId, CancellationToken token)
    {
        var changed = false;
        await transactions.ExecuteAsync(async ct =>
        {
            var operation = await db.RunnerExecutionOperations.SingleAsync(o => o.Id == operationId, ct);
            if (operation.CompletedAtUtc.HasValue) return;
            if (db.Database.IsRelational())
                changed = await db.RunnerExecutionOperations.Where(o => o.Id == operationId && o.CompletedAtUtc == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.CancellationRequested, true), ct) == 1;
            else
            {
                operation.CancellationRequested = true;
                await db.SaveChangesAsync(ct);
                changed = true;
            }
            if (changed) await events.PublishAsync(new RunnerExecutionRequestedEvent(operationId, operation.RunnerId, Management: true), ct);
        }, token);
        return changed;
    }

    public async Task TerminateRunnerAsync(Guid runnerId, CancellationToken token)
    {
        var json = protector.Protect(JsonSerializer.Serialize(
            RunnerExecutionResult.Interrupted("Runner identity was revoked or removed; unfinished work is not repeated."), JsonOptions));
        if (db.Database.IsRelational())
            await db.RunnerExecutionOperations.Where(o => o.RunnerId == runnerId && o.CompletedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ResultJson, json)
                    .SetProperty(o => o.CompletedAtUtc, DateTime.UtcNow), token);
        else
        {
            var pending = await db.RunnerExecutionOperations.Where(o => o.RunnerId == runnerId && o.CompletedAtUtc == null).ToListAsync(token);
            foreach (var operation in pending) { operation.ResultJson = json; operation.CompletedAtUtc = DateTime.UtcNow; }
            await db.SaveChangesAsync(token);
        }
    }

    public async Task<RunnerExecutionAssignment> AssignmentAsync(Guid keyId, Guid operationId, CancellationToken token,
        bool includeRequest = true, Guid authenticatedUserId = default, Guid runnerId = default)
    {
        var operation = await AuthorizeAsync(keyId, operationId, authenticatedUserId, runnerId, token);
        if (operation.CompletedAtUtc.HasValue) return new("already-applied", null);
        var request = includeRequest
            ? JsonSerializer.Deserialize<RunnerExecutionRequest>(protector.Unprotect(operation.ProtectedRequest), JsonOptions)
            : null;
        if (!operation.CancellationRequested && request is not null
            && await TryTerminalizeStaleAssignmentAsync(
                keyId, operation, operationId, request, token, authenticatedUserId, runnerId))
            return new("already-applied", null);

        var actions = new AppActionExecutionResultPersistence(db);
        if (await actions.IsExpiredAsync(operationId, token))
        {
            await ApplyAsync(keyId, operationId, RunnerExecutionResult.Interrupted("Action expired or was terminalized before dispatch."),
                token, authenticatedUserId, runnerId);
            return new("already-applied", null);
        }
        if (includeRequest && !operation.CancellationRequested) await actions.MarkStartedAsync(operationId, token);

        return new(operation.CancellationRequested ? "cancel" : "execute", request);
    }

    private async Task<bool> TryTerminalizeStaleAssignmentAsync(
        Guid keyId,
        RunnerExecutionOperation operation,
        Guid operationId,
        RunnerExecutionRequest request,
        CancellationToken token,
        Guid authenticatedUserId,
        Guid runnerId)
    {
        var failure = await ResolveAssignmentFailureAsync(keyId, operation, request, token);
        if (failure is null) return false;

        RunnerExecutionResult result;
        if (request.HostContainer is not null)
        {
            result = CreateFailedHostContainerResult(failure);
        }
        else if (request.Action is { Kind: "execute" })
        {
            result = MachineActionAssignmentRevalidator.CreateFailedResult(operation.Id, failure);
        }
        else
        {
            result = CreateFailedFileTransferResult(failure);
        }
        await ApplyAsync(keyId, operationId, result, token, authenticatedUserId, runnerId);
        return true;
    }

    private async Task<HostContainerExecutionResponse?> ResolveAssignmentFailureAsync(
        Guid keyId,
        RunnerExecutionOperation operation,
        RunnerExecutionRequest request,
        CancellationToken token)
    {
        if (request.HostContainer is { } containerRequest)
            return await identity.RevalidateHostContainerAssignmentAsync(keyId, containerRequest, token);
        if (request.FileTransfer is { Kind: RunnerFileTransferKinds.DeployPackage } transferRequest)
            return await identity.RevalidateMachineDeploymentAssignmentAsync(
                keyId, operation.RunnerId, operation.RequestedByUserId, transferRequest, token);
        if (request.Action is { Kind: "execute" } actionRequest)
            return await identity.RevalidateMachineActionAssignmentAsync(keyId, operation.RunnerId,
                operation.RequestedByUserId, operation.Id, actionRequest, token);
        return null;
    }

    private static RunnerExecutionResult CreateFailedHostContainerResult(HostContainerExecutionResponse failure)
        => new()
        {
            Outcome = "Failed", Started = DateTime.UtcNow, Finished = DateTime.UtcNow,
            Error = failure.Error, HostContainerResult = failure
        };

    private static RunnerExecutionResult CreateFailedFileTransferResult(HostContainerExecutionResponse failure)
        => new()
        {
            Outcome = "Failed", Started = DateTime.UtcNow, Finished = DateTime.UtcNow,
            Error = failure.Error, FileTransferResult = new RunnerFileTransferResult { Success = false, Error = failure.Error }
        };

    public async Task<RunnerExecutionReceipt> ApplyAsync(Guid keyId, Guid operationId, RunnerExecutionResult result,
        CancellationToken token, Guid authenticatedUserId = default, Guid runnerId = default)
    {
        var operation = await AuthorizeAsync(keyId, operationId, authenticatedUserId, runnerId, token);
        if (operation.CompletedAtUtc.HasValue) return new("already-applied");
        if (result.Outcome is not ("Succeeded" or "Failed" or "Lost" or "Cancelled"))
            throw new ArgumentException("Execution results must be terminal.");
        EnsureHostContainerResult(operation, result);
        var action = new AppActionExecutionResultPersistence(db);
        var projection = await action.ResolveAsync(operation, result, token);
        await EnsureFileTransferResultAsync(operation, result, token);
        var json = protector.Protect(JsonSerializer.Serialize(result, JsonOptions));
        var receipt = new RunnerExecutionReceipt("already-applied");
        await transactions.ExecuteAsync(async ct =>
        {
            if (await PersistResultAsync(operationId, json, ct))
            {
                await action.ApplyAsync(operationId, projection, ct);
                receipt = new("accepted");
            }
        }, token);
        return receipt;
    }

    private void EnsureHostContainerResult(
        RunnerExecutionOperation operation, RunnerExecutionResult result)
    {
        var request = JsonSerializer.Deserialize<RunnerExecutionRequest>(
            protector.Unprotect(operation.ProtectedRequest), JsonOptions);
        if (request?.HostContainer is null)
        {
            if (result.HostContainerResult is not null)
                throw new ArgumentException("Non-container operations cannot return typed container results.");
            return;
        }

        if (result.Outcome is "Lost" or "Cancelled" && result.HostContainerResult is null)
            return;

        if (result.HostContainerResult is null
            || result.Outcome != (result.HostContainerResult.Success ? "Succeeded" : "Failed"))
            throw new ArgumentException("Typed container operations require a matching terminal result.");
    }

    private async Task EnsureFileTransferResultAsync(
        RunnerExecutionOperation operation, RunnerExecutionResult result, CancellationToken token)
    {
        var request = JsonSerializer.Deserialize<RunnerExecutionRequest>(
            protector.Unprotect(operation.ProtectedRequest), JsonOptions);
        var transfer = request?.FileTransfer;
        if (transfer is null)
        {
            if (result.FileTransferResult is not null)
                throw new ArgumentException("Non-transfer operations cannot return file-transfer results.");
            return;
        }

        if (result.Outcome != "Succeeded") return;
        if (result.FileTransferResult is not { Success: true })
            throw new ArgumentException("Successful file-transfer operations require a successful typed result.");
        if (transfer.Kind == RunnerFileTransferKinds.DeployPackage
            && result.FileTransferResult.Deployment is null)
            throw new ArgumentException("Deployment transfers require the deployment result.");
        if (transfer.Kind is RunnerFileTransferKinds.DownloadAppFile or RunnerFileTransferKinds.DownloadMedia)
            await EnsureDownloadArtifactAsync(operation, result.FileTransferResult, token);
    }

    private async Task EnsureDownloadArtifactAsync(
        RunnerExecutionOperation operation, RunnerFileTransferResult transfer, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(transfer.FileName) || transfer.Length is null or < 0
            || transfer.Sha256 is not { Length: 64 } checksum || checksum.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Download transfers require file metadata and a SHA-256 checksum.");

        var path = artifacts.GetRunnerTransferPath(operation.Id, input: false);
        if (!File.Exists(path))
            throw new ArgumentException("Download transfers must upload the result artifact before completing.");
        var file = new FileInfo(path);
        if (file.Length != transfer.Length)
            throw new ArgumentException("Download artifact length does not match its result metadata.");
        var actualChecksum = await DeploymentPackageStore.ComputeSha256Async(path, token);
        if (!string.Equals(actualChecksum, checksum, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Download artifact checksum does not match its result metadata.");
    }

    private async Task<bool> PersistResultAsync(Guid id, string json, CancellationToken token)
    {
        if (db.Database.IsRelational())
            return await db.RunnerExecutionOperations.Where(o => o.Id == id && o.CompletedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ResultJson, json)
                    .SetProperty(o => o.CompletedAtUtc, DateTime.UtcNow), token) == 1;
        var tracked = await db.RunnerExecutionOperations.SingleAsync(o => o.Id == id, token);
        if (tracked.CompletedAtUtc.HasValue) return false;
        tracked.ResultJson = json;
        tracked.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(token);
        return true;
    }

    private async Task<RunnerExecutionOperation> AuthorizeAsync(Guid keyId, Guid operationId,
        Guid authenticatedUserId, Guid runnerId, CancellationToken token)
    {
        if (keyId == Guid.Empty) throw new UnauthorizedAccessException("Runner API-key authentication is required.");
        var operation = await db.RunnerExecutionOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == operationId, token)
            ?? throw new UnauthorizedAccessException("Execution was not found.");
        if (runnerId != Guid.Empty && runnerId != operation.RunnerId)
            throw new UnauthorizedAccessException("Execution is assigned to another Runner.");
        await identity.EnsureAsync(keyId, operation.RunnerId, authenticatedUserId, token);
        return operation;
    }
}
