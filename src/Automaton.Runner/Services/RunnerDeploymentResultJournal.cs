using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerDeploymentResultJournal(RunnerWorkspaceService workspace)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DeployAppPackageResult?> TryReadAsync(
        DeployAppPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DeploymentOperationId is not Guid operationId || request.ExecutionAttemptId is null)
            return null;
        return await TryReadAsync(operationId, cancellationToken);
    }

    public async Task<DeployAppPackageResult?> TryReadAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty)
            return null;
        var path = GetPath(operationId);
        if (!File.Exists(path))
            return null;
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<DeployAppPackageResult>(json, JsonOptions)
            ?? throw new InvalidDataException("The persisted deployment result is invalid.");
    }

    public async Task SaveAsync(
        DeployAppPackageRequest request,
        DeployAppPackageResult result,
        CancellationToken cancellationToken)
    {
        var path = request.DeploymentOperationId is Guid operationId && request.ExecutionAttemptId is not null
            ? GetPath(operationId)
            : null;
        if (path is null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(result, JsonOptions), cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private string GetPath(Guid operationId)
        => Path.Combine(workspace.RunnerRoot, "command-results", $"{operationId:N}.json");
}
