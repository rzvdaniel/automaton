using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class HostedRunnerDeploymentResultJournal(RunnerWorkspaceService workspace)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DeployAppPackageResult?> TryReadAsync(
        DeployAppPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(request);
        if (path is null || !File.Exists(path))
            return null;
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<DeployAppPackageResult>(json, JsonOptions)
            ?? throw new InvalidDataException("The persisted Hosted Runner deployment result is invalid.");
    }

    public async Task SaveAsync(
        DeployAppPackageRequest request,
        DeployAppPackageResult result,
        CancellationToken cancellationToken)
    {
        var path = GetPath(request);
        if (path is null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(result, JsonOptions), cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    public void Delete(Guid operationId, Guid attemptId)
    {
        if (operationId == Guid.Empty || attemptId == Guid.Empty)
            return;
        TryDelete(Path.Combine(workspace.RunnerRoot, "command-results", $"{operationId:N}-{attemptId:N}.json"));
    }

    private string? GetPath(DeployAppPackageRequest request)
    {
        if (request.DeploymentOperationId is not Guid operationId
            || request.ExecutionAttemptId is not Guid attemptId)
            return null;
        return Path.Combine(workspace.RunnerRoot, "command-results", $"{operationId:N}-{attemptId:N}.json");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best-effort after the API has accepted the durable result.
        }
    }
}
