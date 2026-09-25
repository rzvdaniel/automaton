using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal static class AppActionExecutionResultFactory
{
    private static readonly TimeSpan ArtifactRetention = TimeSpan.FromHours(24);

    public static AppActionExecutionResult Create(
        Guid id,
        string status,
        int? exitCode,
        string? errorCode,
        string? summary,
        string? hash,
        DateTime started,
        bool logs,
        bool result)
    {
        var completed = DateTime.UtcNow;
        return new AppActionExecutionResult
        {
            ExecutionId = id,
            Status = status,
            ExitCode = exitCode,
            ErrorCode = errorCode,
            FailureSummary = summary,
            DeploymentHash = hash,
            StartedAtUtc = started,
            CompletedAtUtc = completed,
            LogsAvailable = logs,
            ResultAvailable = result,
            LogsExpireAtUtc = logs ? completed + ArtifactRetention : null,
            ResultExpireAtUtc = result ? completed + ArtifactRetention : null
        };
    }
}
