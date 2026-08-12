using System.Text.Json;
using Automaton.AgentOrchestrator.Domain;

namespace Automaton.AgentOrchestrator.Integration;

public sealed class FileOrchestratorStatusQuery(string statusPath) : IOrchestratorStatusQuery
{
    private const long MaximumStatusBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string path = Path.GetFullPath(statusPath);

    public async Task<OrchestratorStatusQueryResult> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return Unavailable("operator status is not available");
            if (info.Length is <= 0 or > MaximumStatusBytes) return Unavailable("operator status size is invalid");
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 16_384, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var source = await JsonSerializer.DeserializeAsync<OperatorStatusSnapshot>(stream, Json, cancellationToken);
            if (source is null || source.SchemaVersion != 1) return Unavailable("operator status schema is unsupported");
            return new(OrchestratorIntegrationSchema.CurrentVersion, OrchestratorAvailabilityStates.Available, Map(source));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Unavailable("operator status could not be read safely");
        }
    }

    public async Task<OrchestratorRunContract?> GetRunAsync(string workItemId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        var result = await GetAsync(cancellationToken);
        return result.Status?.Runs.SingleOrDefault(run => string.Equals(run.WorkItemId, workItemId, StringComparison.Ordinal));
    }

    private static OrchestratorStatusContract Map(OperatorStatusSnapshot value) => new(
        value.GeneratedAtUtc, value.DaemonState, value.StartupReconciled, value.Healthy, value.RepositoryIdentity,
        value.ApplicationVersion, value.BuildRevision, value.Runs.Select(Map).ToArray(), value.LastError);

    private static OrchestratorRunContract Map(OperatorRunStatus value) => new(
        value.WorkItemId, value.RunState, value.Attempt, value.UpdatedAtUtc, value.Stage, value.Role, value.WorkspaceKey,
        value.WorkspacePath, value.Branch, value.PullRequestUrl, value.HeadSha, value.CiStatus, value.FeedbackStatus,
        value.OwnershipStatus, value.RetryAtUtc, value.Reason, value.CiObservedAtUtc, value.FeedbackObservedAtUtc);

    private static OrchestratorStatusQueryResult Unavailable(string reason) =>
        new(OrchestratorIntegrationSchema.CurrentVersion, OrchestratorAvailabilityStates.Unavailable, Reason: reason);
}
