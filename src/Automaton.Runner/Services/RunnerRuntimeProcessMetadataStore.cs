using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerRuntimeProcessMetadataStore(
    RunnerWorkspaceService workspace,
    Func<RunnerRuntimeDeploymentMetadata, Exception?>? writeFailureFactory)
{
    private const string MetadataFileName = "deployment.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string GetPath(Guid appId, Guid targetId)
        => Path.Combine(workspace.GetAppAutomationRoot(appId, targetId), MetadataFileName);

    public async Task<RunnerRuntimeDeploymentMetadata?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        var metadata = JsonSerializer.Deserialize<RunnerRuntimeDeploymentMetadata>(
            await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
        if (metadata is null || (metadata.Runtime is null && metadata.Candidate?.Runtime is null))
            throw new InvalidOperationException("Runtime deployment metadata is invalid.");
        return metadata;
    }

    public async Task WriteAsync(RunnerRuntimeDeploymentMetadata metadata, CancellationToken cancellationToken)
    {
        var injectedFailure = writeFailureFactory?.Invoke(metadata);
        if (injectedFailure is not null) throw injectedFailure;

        var path = GetPath(Guid.Parse(metadata.AppId), Guid.Parse(metadata.DeploymentTargetId));
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{MetadataFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(JsonSerializer.Serialize(metadata, JsonOptions).AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
