using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Serilog;
using CustomStep = Automaton.Api.Models.CustomStep;

namespace Automaton.Api.Services;

public sealed class CustomStepRunnerExecutionService(
    ApplicationDbContext dataContext,
    IHubContext<AutomatonHub> automatonHub)
{
    private readonly Serilog.ILogger logger = Log.ForContext<CustomStepRunnerExecutionService>();

    public Task EnsureCanExecuteAsync(
        IReadOnlyCollection<Guid> selectedRunnerIds,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return EnsureInfrastructureRunnerAccessAsync(selectedRunnerIds, userId, cancellationToken);
    }

    public async Task<IReadOnlyList<RunnerCustomStepResult>> ExecuteOnRunnersAsync(
        CustomStep customStep,
        IReadOnlyList<Guid> selectedRunnerIds,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var results = new List<RunnerCustomStepResult>(selectedRunnerIds.Count);
        foreach (var runnerId in selectedRunnerIds)
        {
            results.Add(await ExecuteOnRunnerAsync(customStep, runnerId, userId, cancellationToken));
        }

        return results;
    }

    private async Task EnsureInfrastructureRunnerAccessAsync(
        IReadOnlyCollection<Guid> selectedRunnerIds,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var selectedRunners = await dataContext.Runners
            .Where(runner => selectedRunnerIds.Contains(runner.Id))
            .Select(runner => new { runner.CapabilitiesJson, runner.Purpose })
            .ToListAsync(cancellationToken);
        var infrastructureRunnerSelected = selectedRunners.Any(runner =>
            RunnerCapabilities.RequiresAdministrator(runner.CapabilitiesJson, runner.Purpose));

        if (infrastructureRunnerSelected && !await IsAdministratorAsync(userId, cancellationToken))
        {
            throw new UnauthorizedAccessException("Only administrators may execute custom steps on Infrastructure Runners.");
        }
    }

    private Task<bool> IsAdministratorAsync(Guid userId, CancellationToken cancellationToken)
    {
        return dataContext.UserRoles
            .Join(dataContext.Roles, userRole => userRole.RoleId, role => role.Id, (userRole, role) => new { userRole.UserId, role.Name })
            .AnyAsync(candidate => candidate.UserId == userId &&
                                   (candidate.Name == UserRole.Administrator || candidate.Name == UserRole.SystemAdministrator),
                cancellationToken);
    }

    private async Task<RunnerCustomStepResult> ExecuteOnRunnerAsync(
        CustomStep customStep,
        Guid runnerId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var runner = GetRunner(runnerId, userId);
            if (runner is null)
            {
                logger.Warning("Runner {RunnerId} not found for user {UserId}", runnerId, userId);
                return CreateFailureResult(customStep.Id, runnerId, "Runner not found");
            }

            if (!RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow))
            {
                logger.Warning("Runner {RunnerId} ({RunnerName}) is not connected", runnerId, runner.Name);
                return CreateFailureResult(customStep.Id, runnerId, "Runner is not connected");
            }

            return await InvokeRunnerAsync(customStep, runner, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Error executing custom step {CustomStepId} on runner {RunnerId}", customStep.Id, runnerId);
            return CreateFailureResult(customStep.Id, runnerId, $"Execution failed: {exception.Message}");
        }
    }

    private async Task<RunnerCustomStepResult> InvokeRunnerAsync(
        CustomStep customStep,
        Runner runner,
        CancellationToken cancellationToken)
    {
        logger.Debug(
            "Executing custom step {CustomStepId} ({CustomStepName}) on runner {RunnerId} ({RunnerName})",
            customStep.Id,
            customStep.Name,
            runner.Id,
            runner.Name);

        var response = await automatonHub.Clients.Client(runner.ConnectionId).InvokeAsync<CustomStepExecutionResponse>(
            AutomatonHubMethods.RunCustomStep,
            CreatePayload(customStep),
            cancellationToken);

        LogExecutionOutcome(customStep.Id, runner.Id, response);
        return MapResult(customStep.Id, runner.Id, response);
    }

    private static object CreatePayload(CustomStep customStep)
    {
        return new
        {
            Id = customStep.Id,
            Name = customStep.Name,
            Code = customStep.Definition.Code,
            Language = customStep.Definition.Language,
            CodeInputVariables = customStep.Definition.CodeInputVariables,
            CodeOutputVariables = customStep.Definition.CodeOutputVariables
        };
    }

    private void LogExecutionOutcome(Guid customStepId, Guid runnerId, CustomStepExecutionResponse response)
    {
        if (response.Success)
        {
            logger.Debug("Custom step {CustomStepId} completed successfully on runner {RunnerId}", customStepId, runnerId);
            return;
        }

        logger.Warning("Custom step {CustomStepId} failed on runner {RunnerId}: {Error}", customStepId, runnerId, response.Error);
    }

    private static RunnerCustomStepResult MapResult(Guid customStepId, Guid runnerId, CustomStepExecutionResponse response)
    {
        return new RunnerCustomStepResult
        {
            CustomStepId = customStepId,
            RunnerId = runnerId,
            Success = response.Success,
            Output = response.Output,
            Error = response.Error,
            Started = response.Started,
            Finished = response.Finished,
            OutputVariables = response.OutputVariables
        };
    }

    private Runner GetRunner(Guid id, Guid userId)
    {
        var runner = dataContext.Runners.SingleOrDefault(candidate =>
            candidate.Id == id && candidate.RunnerUsers.Any(runnerUser => runnerUser.UserId == userId));
        if (runner is not null)
        {
            dataContext.Entry(runner).Reload();
        }

        return runner;
    }

    private static RunnerCustomStepResult CreateFailureResult(Guid customStepId, Guid runnerId, string error)
    {
        return new RunnerCustomStepResult
        {
            CustomStepId = customStepId,
            RunnerId = runnerId,
            Success = false,
            Error = error,
            Started = DateTime.UtcNow,
            Finished = DateTime.UtcNow
        };
    }
}
