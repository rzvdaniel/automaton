using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Dispatches persisted App Action executions to their assigned Runner.</summary>
internal sealed class AppActionExecutionDispatcher(
    ApplicationDbContext db,
    IHubContext<AutomatonHub> hub)
{
    public async Task DispatchAsync(Guid executionId, string connectionId, string inputJson)
    {
        var execution = await db.AppModuleExecutions.SingleOrDefaultAsync(item => item.Id == executionId);
        if (execution == null) return;
        try
        {
            var deploymentTargetId = execution.DeploymentTargetId
                ?? throw new InvalidOperationException("Runner Action execution has no deployment target.");
            execution.Status = AppModuleExecutionStatuses.Running;
            execution.StartedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            using var inputDocument = JsonDocument.Parse(inputJson);
            var result = await InvokeRunnerAsync(execution, deploymentTargetId, connectionId, inputDocument.RootElement);
            ApplyResult(execution, result);
        }
        catch (Exception ex)
        {
            execution.Status = AppModuleExecutionStatuses.Failed;
            execution.ErrorCode = AppModuleExecutionErrorCodes.PlatformError;
            execution.FailureSummary = Limit(ex.Message, 1000);
            execution.CompletedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<AppActionExecutionResult> InvokeRunnerAsync(
        AppModuleExecution execution,
        Guid deploymentTargetId,
        string connectionId,
        JsonElement input)
    {
        var runnerPurpose = await db.Runners.AsNoTracking()
            .Where(item => item.Id == execution.RunnerId)
            .Select(item => item.Purpose)
            .SingleOrDefaultAsync();
        var request = new ExecuteAppActionModuleRequest
        {
            ExecutionId = execution.Id,
            AppId = execution.AppId,
            ActionKey = execution.ModuleKey,
            DeploymentTargetId = deploymentTargetId,
            ExpectedDeploymentHash = execution.DeploymentHash,
            Input = input.Clone(),
            SystemInfrastructure = InfrastructureAppTrust.IsTrustedApp(execution.AppId)
                && string.Equals(runnerPurpose, RunnerPurposes.Infrastructure, StringComparison.Ordinal)
        };
        return await hub.Clients.Client(connectionId).InvokeAsync<AppActionExecutionResult>(
            AutomatonHubMethods.ExecuteAppActionModule,
            request,
            CancellationToken.None);
    }

    private static void ApplyResult(AppModuleExecution execution, AppActionExecutionResult result)
    {
        execution.Status = result.Status;
        execution.DeploymentHash = result.DeploymentHash;
        execution.ExitCode = result.ExitCode;
        execution.ErrorCode = result.ErrorCode;
        execution.FailureSummary = Limit(result.FailureSummary, 1000);
        execution.StartedAtUtc = result.StartedAtUtc;
        execution.CompletedAtUtc = result.CompletedAtUtc;
        execution.LogsAvailableUntilUtc = result.LogsExpireAtUtc;
        execution.ResultAvailableUntilUtc = result.ResultExpireAtUtc;
    }

    private static string? Limit(string? value, int max)
        => value?.Length > max ? value[..max] : value;
}
