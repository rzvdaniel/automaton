using System.Security.Cryptography;
using Automaton.Core.Models;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerFileTransferExecutor(IServiceScopeFactory scopes)
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public async Task<RunnerExecutionResult> ExecuteAsync(
        Guid operationId, RunnerFileTransferCommand command, RunnerExecutionApiClient api, CancellationToken token)
    {
        var result = command.Kind switch
        {
            RunnerFileTransferKinds.UploadAppFile => await UploadAppFileAsync(operationId, command, api, token),
            RunnerFileTransferKinds.UploadMedia => await UploadMediaAsync(operationId, command, api, token),
            RunnerFileTransferKinds.ReadMediaStats => ReadMediaStats(command, token),
            RunnerFileTransferKinds.ReadMediaMetadata => ReadMediaMetadata(command, token),
            RunnerFileTransferKinds.DownloadAppFile => await DownloadAppFileAsync(operationId, command, api, token),
            RunnerFileTransferKinds.DownloadMedia => await DownloadMediaAsync(operationId, command, api, token),
            RunnerFileTransferKinds.DeployPackage => await DeployPackageAsync(operationId, command, api, token),
            _ => throw new InvalidDataException($"Unsupported Runner file-transfer operation '{command.Kind}'.")
        };
        return ToExecutionResult(result);
    }

    private async Task<RunnerFileTransferResult> UploadAppFileAsync(
        Guid id, RunnerFileTransferCommand command, RunnerExecutionApiClient api, CancellationToken token)
    {
        var name = SafeFileName(command.FileName);
        var directory = Path.Combine(Path.GetTempPath(), "uploads", id.ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        await DownloadInputAsync(id, command, api, path, token);
        return new() { Success = true, SessionId = command.SessionId, FileName = name, Length = command.Length };
    }

    private async Task<RunnerFileTransferResult> UploadMediaAsync(
        Guid id, RunnerFileTransferCommand command, RunnerExecutionApiClient api, CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var workspace = scope.ServiceProvider.GetRequiredService<RunnerWorkspaceService>();
        var path = workspace.ResolveAppMediaFilePath(command.AppId, Required(command.RelativePath, "Media path"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await DownloadInputAsync(id, command, api, path, token);
        return new() { Success = true, RelativePath = command.RelativePath, FileName = Path.GetFileName(path), Length = command.Length };
    }

    private RunnerFileTransferResult ReadMediaStats(RunnerFileTransferCommand command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var scope = scopes.CreateScope();
        var workspace = scope.ServiceProvider.GetRequiredService<RunnerWorkspaceService>();
        var directory = Path.Combine(workspace.GetLegacyControllerDataDirectory(command.AppId), "media");
        if (!Directory.Exists(directory)) return new() { Success = true, TotalBytes = 0, FileCount = 0 };
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToArray();
        return new() { Success = true, FileCount = files.Length, TotalBytes = files.Sum(path => new FileInfo(path).Length) };
    }

    private RunnerFileTransferResult ReadMediaMetadata(RunnerFileTransferCommand command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var scope = scopes.CreateScope();
        var workspace = scope.ServiceProvider.GetRequiredService<RunnerWorkspaceService>();
        var path = workspace.ResolveAppMediaFilePath(command.AppId, Required(command.RelativePath, "Media path"));
        if (!File.Exists(path)) return new() { StatusCode = 404, Error = "File not found." };
        var info = new FileInfo(path);
        return new()
        {
            Success = true, StatusCode = 200, FileName = info.Name,
            ContentType = GetContentType(info.Name), Length = info.Length
        };
    }

    private async Task<RunnerFileTransferResult> DownloadAppFileAsync(
        Guid id, RunnerFileTransferCommand command, RunnerExecutionApiClient api, CancellationToken token)
    {
        var session = SafeSessionId(command.SessionId);
        var directory = Path.Combine(Path.GetTempPath(), "results", session);
        var path = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory).OrderBy(candidate => candidate, StringComparer.Ordinal).FirstOrDefault()
            : null;
        if (path is null) return new() { Success = false, StatusCode = 404, Error = "File not found." };
        return await UploadOutputAsync(id, path, api, token);
    }

    private async Task<RunnerFileTransferResult> DownloadMediaAsync(
        Guid id, RunnerFileTransferCommand command, RunnerExecutionApiClient api, CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var workspace = scope.ServiceProvider.GetRequiredService<RunnerWorkspaceService>();
        var path = workspace.ResolveAppMediaFilePath(command.AppId, Required(command.RelativePath, "Media path"));
        if (!File.Exists(path)) return new() { Success = false, StatusCode = 404, Error = "File not found." };
        return await UploadOutputAsync(id, path, api, token);
    }

    private async Task<RunnerFileTransferResult> DeployPackageAsync(
        Guid id, RunnerFileTransferCommand command, RunnerExecutionApiClient api, CancellationToken token)
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"automaton-deploy-{id:N}.zip");
        try
        {
            await DownloadInputAsync(id, command, api, packagePath, token);
            using var scope = scopes.CreateScope();
            var deployment = await scope.ServiceProvider.GetRequiredService<RunnerDeploymentService>().DeployPackage(
                new DeployAppPackageRequest
                {
                    AppId = command.AppId,
                    DeploymentTargetId = command.DeploymentTargetId ?? throw new InvalidDataException("Deployment target is required."),
                    PackagePath = packagePath,
                    Clean = command.Clean,
                    DeploymentOperationId = command.DeploymentOperationId,
                    ExecutionAttemptId = command.ExecutionAttemptId,
                    SystemInfrastructure = command.SystemInfrastructure,
                    ActionContainerPolicy = command.ActionContainerPolicy
                });
            return new() { Success = deployment.Success, Error = deployment.Message, Deployment = deployment };
        }
        finally
        {
            TryDelete(packagePath);
        }
    }

    private static async Task DownloadInputAsync(
        Guid id, RunnerFileTransferCommand command, RunnerExecutionApiClient api, string destination, CancellationToken token)
    {
        var staging = destination + $".{id:N}.partial";
        try
        {
            await api.DownloadInputArtifactAsync(id, staging, token);
            var info = new FileInfo(staging);
            if (command.Length != info.Length)
                throw new InvalidDataException("Runner input artifact length did not match its pinned request.");
            if (!string.IsNullOrWhiteSpace(command.Sha256))
            {
                await using var content = File.OpenRead(staging);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(content, token));
                if (!string.Equals(actual, command.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Runner input artifact checksum did not match its pinned request.");
            }
            File.Move(staging, destination, overwrite: true);
        }
        finally { TryDelete(staging); }
    }

    private static async Task<RunnerFileTransferResult> UploadOutputAsync(
        Guid id, string path, RunnerExecutionApiClient api, CancellationToken token)
    {
        var info = new FileInfo(path);
        if (info.Length > RunnerFileTransferBounds.MaximumBytes)
            throw new InvalidDataException("Runner output artifact exceeds the 200 MiB transfer limit.");
        var name = info.Name;
        var contentType = GetContentType(name);
        await api.UploadOutputArtifactAsync(id, path, token);
        await using var content = File.OpenRead(path);
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(content, token));
        return new() { Success = true, FileName = name, ContentType = contentType, Length = info.Length, Sha256 = sha256 };
    }

    private static RunnerExecutionResult ToExecutionResult(RunnerFileTransferResult result)
        => new()
        {
            Outcome = result.Success ? "Succeeded" : "Failed",
            Error = result.Error,
            Started = DateTime.UtcNow,
            Finished = DateTime.UtcNow,
            FileTransferResult = result
        };

    private static string Required(string? value, string label)
        => string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException($"{label} is required.") : value;

    private static string SafeFileName(string? value)
    {
        var name = Path.GetFileName((value ?? string.Empty).Replace('\0', '_').Trim());
        return string.IsNullOrWhiteSpace(name) ? throw new InvalidDataException("File name is required.") : name;
    }

    private static string SafeSessionId(string value)
    {
        if (value.Length is < 1 or > 128 || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            throw new InvalidDataException("File session ID is invalid.");
        return value;
    }

    private static string GetContentType(string name)
        => ContentTypes.TryGetContentType(name, out var type) ? type : "application/octet-stream";

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
