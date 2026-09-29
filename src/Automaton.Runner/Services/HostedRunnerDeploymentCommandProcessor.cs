using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class HostedRunnerDeploymentCommandProcessor(
    FileStorageService storage,
    RunnerDeploymentService deployment,
    HostedRunnerDeploymentPackageDownloader downloader)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DeployAppPackageResult> ProcessAsync(
        HostedRunnerDeploymentCommand command,
        HostedRunnerCommandApiClient api,
        CancellationToken cancellationToken)
    {
        var journalPath = GetJournalPath(command);
        if (File.Exists(journalPath))
            return await ReadJournalAsync(journalPath, cancellationToken);

        string packagePath;
        try
        {
            packagePath = await downloader.DownloadAsync(
                command, api.ApiUrl, api.ApiKey, cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var unavailable = new DeployAppPackageResult
            {
                DeploymentTargetId = command.DeploymentTargetId,
                Success = false,
                Message = exception.Message
            };
            await WriteJournalAsync(journalPath, unavailable, cancellationToken);
            return unavailable;
        }
        try
        {
            var result = await deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = command.AppId,
                DeploymentTargetId = command.DeploymentTargetId,
                PackagePath = packagePath,
                Clean = command.Clean,
                DeploymentOperationId = command.OperationId,
                ExecutionAttemptId = command.ExecutionAttemptId
            });
            await WriteJournalAsync(journalPath, result, cancellationToken);
            return result;
        }
        finally
        {
            TryDelete(packagePath);
        }
    }

    public void DeleteJournal(HostedRunnerDeploymentCommand command) => TryDelete(GetJournalPath(command));

    public void DeleteExecutionResult(HostedRunnerDeploymentCommand command)
        => deployment.ForgetHostedRunnerDeploymentResult(command.OperationId, command.ExecutionAttemptId);

    private static async Task<DeployAppPackageResult> ReadJournalAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var saved = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<DeployAppPackageResult>(saved, JsonOptions)
            ?? throw new InvalidDataException("The saved deployment result is invalid.");
    }

    private static async Task WriteJournalAsync(
        string path,
        DeployAppPackageResult result,
        CancellationToken cancellationToken)
    {
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(result, JsonOptions), cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }

    private string GetJournalPath(HostedRunnerDeploymentCommand command)
        => storage.GetHostedRunnerCommandResultPath(command.OperationId, command.ExecutionAttemptId);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best-effort; the next command retry can safely overwrite this journal.
        }
    }
}
