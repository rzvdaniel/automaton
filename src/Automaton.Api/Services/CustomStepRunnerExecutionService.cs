using Automaton.Core.Models;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;
using CustomStep = Automaton.Api.Models.CustomStep;

namespace Automaton.Api.Services;

public sealed class CustomStepRunnerExecutionService(
    ApplicationDbContext dataContext,
    RunnerExecutionDispatchService dispatch)
{
    private readonly Serilog.ILogger logger = Log.ForContext<CustomStepRunnerExecutionService>();

    public Task EnsureCanExecuteAsync(
        IReadOnlyCollection<Guid> selectedRunnerIds,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return EnsureNoRunnerExecutionAsync(selectedRunnerIds, userId, cancellationToken);
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

    private static Task EnsureNoRunnerExecutionAsync(
        IReadOnlyCollection<Guid> selectedRunnerIds,
        Guid userId,
        CancellationToken cancellationToken)
    {
        _ = userId;
        cancellationToken.ThrowIfCancellationRequested();
        if (selectedRunnerIds.Count > 0)
            throw new UnauthorizedAccessException("Custom-step execution is disabled on Machine Management Runners.");
        return Task.CompletedTask;
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

            if ((!runner.LastHeartbeatAtUtc.HasValue || runner.LastHeartbeatAtUtc < DateTime.UtcNow - RunnerPresenceEvaluator.OfflineThreshold))
            {
                logger.Warning("Runner {RunnerId} ({RunnerName}) is not connected", runnerId, runner.Name);
                return CreateFailureResult(customStep.Id, runnerId, "Runner is not connected");
            }

            return await InvokeRunnerAsync(customStep, runner, userId, cancellationToken);
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
        Guid userId,
        CancellationToken cancellationToken)
    {
        logger.Debug(
            "Executing custom step {CustomStepId} ({CustomStepName}) on runner {RunnerId} ({RunnerName})",
            customStep.Id,
            customStep.Name,
            runner.Id,
            runner.Name);

        var response = await dispatch.ExecuteAsync(runner.Id, userId,
            new(null, CreatePayload(customStep)), cancellationToken);

        LogExecutionOutcome(customStep.Id, runner.Id, response);
        return MapResult(customStep.Id, runner.Id, response);
    }

    internal static RunnerCustomStepPayload CreatePayload(CustomStep customStep)
    {
        return new RunnerCustomStepPayload
        {
            Id = customStep.Id,
            Name = customStep.Name,
            Code = customStep.Definition.Code ?? string.Empty,
            Language = customStep.Definition.Language,
            CodeInputVariables = customStep.Definition.CodeInputVariables?.ToList(),
            CodeOutputVariables = customStep.Definition.CodeOutputVariables?.ToList()
        };
    }

    private void LogExecutionOutcome(Guid customStepId, Guid runnerId, RunnerExecutionResult response)
    {
        if (response.Outcome == "Succeeded")
        {
            logger.Debug("Custom step {CustomStepId} completed successfully on runner {RunnerId}", customStepId, runnerId);
            return;
        }

        logger.Warning("Custom step {CustomStepId} failed on runner {RunnerId}: {Error}", customStepId, runnerId, response.Error);
    }

    private static RunnerCustomStepResult MapResult(Guid customStepId, Guid runnerId, RunnerExecutionResult response)
    {
        return new RunnerCustomStepResult
        {
            CustomStepId = customStepId,
            RunnerId = runnerId,
            Success = response.Outcome == "Succeeded",
            Output = response.Output,
            Error = response.Error,
            Started = response.Started,
            Finished = response.Finished,
            OutputVariables = response.OutputVariables.ToDictionary(p => p.Key, p => p.Value ?? string.Empty)
        };
    }

    private Runner? GetRunner(Guid id, Guid userId)
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
