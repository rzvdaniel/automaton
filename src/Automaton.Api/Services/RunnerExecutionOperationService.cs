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
    IDataProtectionProvider protection, RunnerExecutionIdentityService identity)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector protector = protection.CreateProtector("RunnerExecutionOperation.v1");

    public async Task<Guid> QueueAsync(Guid runnerId, Guid actor, RunnerExecutionRequest request, CancellationToken token)
    {
        var operation = new RunnerExecutionOperation
        {
            Id = Guid.NewGuid(), RunnerId = runnerId, RequestedByUserId = actor,
            ProtectedRequest = protector.Protect(JsonSerializer.Serialize(request, JsonOptions))
        };
        await transactions.ExecuteAsync(async ct =>
        {
            db.RunnerExecutionOperations.Add(operation);
            await db.SaveChangesAsync(ct);
            await events.PublishAsync(new RunnerExecutionRequestedEvent(operation.Id, runnerId), ct);
        }, token);
        return operation.Id;
    }

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

    public async Task CancelAsync(Guid operationId, CancellationToken token)
    {
        if (db.Database.IsRelational())
            await db.RunnerExecutionOperations.Where(o => o.Id == operationId && o.CompletedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.CancellationRequested, true), token);
        else
        {
            var operation = await db.RunnerExecutionOperations.SingleAsync(o => o.Id == operationId, token);
            if (operation.CompletedAtUtc is null) operation.CancellationRequested = true;
            await db.SaveChangesAsync(token);
        }
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
        return new(operation.CancellationRequested ? "cancel" : "execute",
            includeRequest ? JsonSerializer.Deserialize<RunnerExecutionRequest>(protector.Unprotect(operation.ProtectedRequest), JsonOptions) : null);
    }

    public async Task<RunnerExecutionReceipt> ApplyAsync(Guid keyId, Guid operationId, RunnerExecutionResult result,
        CancellationToken token, Guid authenticatedUserId = default, Guid runnerId = default)
    {
        var operation = await AuthorizeAsync(keyId, operationId, authenticatedUserId, runnerId, token);
        if (operation.CompletedAtUtc.HasValue) return new("already-applied");
        if (result.Outcome is not ("Succeeded" or "Failed" or "Lost" or "Cancelled"))
            throw new ArgumentException("Execution results must be terminal.");
        var json = protector.Protect(JsonSerializer.Serialize(result, JsonOptions));
        if (db.Database.IsRelational())
        {
            var updated = await db.RunnerExecutionOperations.Where(o => o.Id == operationId && o.CompletedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ResultJson, json)
                    .SetProperty(o => o.CompletedAtUtc, DateTime.UtcNow), token);
            return new(updated == 1 ? "accepted" : "already-applied");
        }
        var tracked = await db.RunnerExecutionOperations.SingleAsync(o => o.Id == operationId, token);
        tracked.ResultJson = json;
        tracked.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(token);
        return new("accepted");
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
