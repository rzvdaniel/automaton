using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Projects a durable action result into the existing application execution record in the result transaction.</summary>
internal sealed class AppActionExecutionResultPersistence(ApplicationDbContext db)
{
    public async Task<bool> IsExpiredAsync(Guid id, CancellationToken token)
    {
        var action = await db.AppModuleExecutions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, token);
        return action is not null && (action.CompletedAtUtc.HasValue || action.ExecutionDeadlineUtc <= DateTime.UtcNow);
    }
    public async Task MarkStartedAsync(Guid id, CancellationToken token)
    {
        if (db.Database.IsRelational())
        {
            await db.AppModuleExecutions.Where(e => e.Id == id && e.Status == AppModuleExecutionStatuses.Dispatching)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, AppModuleExecutionStatuses.Running)
                    .SetProperty(e => e.StartedAtUtc, DateTime.UtcNow), token);
            return;
        }
        var action = await db.AppModuleExecutions.SingleOrDefaultAsync(e => e.Id == id, token);
        if (action?.Status != AppModuleExecutionStatuses.Dispatching) return;
        action.Status = AppModuleExecutionStatuses.Running;
        action.StartedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(token);
    }
    public async Task<AppActionExecutionResult?> ResolveAsync(RunnerExecutionOperation operation, RunnerExecutionResult result, CancellationToken token)
    {
        var execution = await db.AppModuleExecutions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == operation.Id, token);
        if (execution is null) return null;
        if (execution.RunnerId != operation.RunnerId) throw new UnauthorizedAccessException("Action Runner identity differs from the operation.");
        var action = result.ActionResult?.Deserialize<AppActionExecutionResult>();
        if (action is null)
            return new() { ExecutionId = operation.Id, Status = result.Outcome is "Cancelled" or "Failed" ? result.Outcome : "Lost",
                ErrorCode = result.Outcome == "Failed" ? AppModuleExecutionErrorCodes.PlatformError : AppModuleExecutionErrorCodes.AmbiguousDispatch, FailureSummary = result.Error,
                StartedAtUtc = result.Started, CompletedAtUtc = result.Finished };
        if (action.ExecutionId != operation.Id || !IsTerminal(action.Status))
            throw new ArgumentException("Action results require the assigned execution identity and a terminal status.");
        return action;
    }
    private static bool IsTerminal(string status)
        => status is "Succeeded" or "Failed" or "TimedOut" or "Cancelled" or "RunnerUnavailable" or "Lost";
    public async Task ApplyAsync(Guid id, AppActionExecutionResult? result, CancellationToken token)
    {
        if (result is null) return;
        var summary = result.FailureSummary?.Length > 1000 ? result.FailureSummary[..1000] : result.FailureSummary;
        if (db.Database.IsRelational())
        {
            await db.AppModuleExecutions.Where(e => e.Id == id && e.CompletedAtUtc == null).ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, result.Status).SetProperty(e => e.DeploymentHash, result.DeploymentHash)
                .SetProperty(e => e.ExitCode, result.ExitCode).SetProperty(e => e.ErrorCode, result.ErrorCode)
                .SetProperty(e => e.FailureSummary, summary).SetProperty(e => e.StartedAtUtc, result.StartedAtUtc)
                .SetProperty(e => e.CompletedAtUtc, result.CompletedAtUtc).SetProperty(e => e.LogsAvailableUntilUtc, result.LogsExpireAtUtc)
                .SetProperty(e => e.ResultAvailableUntilUtc, result.ResultExpireAtUtc), token);
            return;
        }
        var execution = await db.AppModuleExecutions.SingleAsync(e => e.Id == id, token);
        if (execution.CompletedAtUtc.HasValue) return;
        execution.Status = result.Status; execution.DeploymentHash = result.DeploymentHash;
        execution.ExitCode = result.ExitCode; execution.ErrorCode = result.ErrorCode; execution.FailureSummary = summary;
        execution.StartedAtUtc = result.StartedAtUtc; execution.CompletedAtUtc = result.CompletedAtUtc;
        execution.LogsAvailableUntilUtc = result.LogsExpireAtUtc; execution.ResultAvailableUntilUtc = result.ResultExpireAtUtc;
        await db.SaveChangesAsync(token);
    }
}
