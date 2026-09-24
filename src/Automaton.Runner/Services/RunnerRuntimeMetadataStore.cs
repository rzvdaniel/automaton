using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerRuntimeMetadataStore(RunnerWorkspaceService workspace)
{
    private const string MetadataFileName = "deployment.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string GetPath(Guid appId, Guid targetId)
        => Path.Combine(workspace.GetAppAutomationRoot(appId, targetId), MetadataFileName);

    public async Task<RunnerRuntimeDeploymentMetadata?> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<RunnerRuntimeDeploymentMetadata>(
            await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
    }

    public async Task WriteAsync(
        RunnerRuntimeDeploymentMetadata metadata,
        CancellationToken cancellationToken)
    {
        var path = GetPath(Guid.Parse(metadata.AppId), Guid.Parse(metadata.DeploymentTargetId));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(metadata, JsonOptions), cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    public RunnerRuntimeDeploymentMetadata Clone(RunnerRuntimeDeploymentMetadata metadata)
        => JsonSerializer.Deserialize<RunnerRuntimeDeploymentMetadata>(
            JsonSerializer.Serialize(metadata, JsonOptions), JsonOptions)
            ?? throw new InvalidOperationException("Could not clone runtime metadata.");
}
