using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public sealed class HostedRunnerDeploymentCommandService(
    ApplicationDbContext db,
    DeploymentPackageStore packageStore,
    HostedRunnerDeploymentCommandPublisher publisher,
    AppDeploymentTargetStateService targetState,
    IOptions<HostedRunnerOptions> hostedRunnerOptions) : IHostedRunnerDeploymentCommandDispatcher
{
    public async Task<bool> PublishAsync(
        DeploymentOperation operation,
        Guid executionAttemptId,
        CancellationToken cancellationToken)
    {
        var target = await ResolveTargetAsync(operation, cancellationToken);
        if (target?.ExecutionRunner?.Purpose != RunnerPurposes.Managed)
            return false;

        await EnsureRunnerCanDeployAsync(operation, target.ExecutionRunner, cancellationToken);
        var apiBase = CreateHttpsApiBaseUri();
        var length = new FileInfo(operation.PackagePath).Length;
        var sha256 = await packageStore.ComputeSha256Async(operation.PackagePath, cancellationToken);
        var packageUrl = new Uri(apiBase,
            $"api/hosted-runner-commands/operations/{operation.Id:D}/package?attemptId={executionAttemptId:D}");
        await targetState.MarkStartedAsync(
            operation.WebAppId, target.Id, cancellationToken, operation.Id, executionAttemptId);
        try
        {
            await publisher.PublishAsync(new HostedRunnerDeploymentCommand(
                operation.Id,
                executionAttemptId,
                target.ExecutionRunner.Id,
                operation.WebAppId,
                target.Id,
                packageUrl.AbsoluteUri,
                length,
                sha256,
                operation.Clean,
                DateTime.UtcNow), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new HostedRunnerDeploymentPublishException(operation.Id, exception);
        }
        return true;
    }

    private Uri CreateHttpsApiBaseUri()
    {
        var serverUrl = hostedRunnerOptions.Value.ServerUrl;
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var apiBase)
            || apiBase.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Hosted Runner deployment package downloads require an HTTPS API URL.");
        return new Uri(apiBase.AbsoluteUri.TrimEnd('/') + "/");
    }

    public async Task<FileStream?> OpenPackageAsync(
        Guid apiKeyId,
        Guid operationId,
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        var runnerId = await GetHostedRunnerIdAsync(apiKeyId, cancellationToken);
        if (runnerId is null)
            return null;
        var operation = await db.DeploymentOperations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == operationId
                && item.Status == DeploymentOperationStatuses.Running
                && item.ExecutionAttemptId == attemptId, cancellationToken);
        if (operation is null)
            return null;
        var target = await ResolveTargetAsync(operation, cancellationToken);
        if (target?.ExecutionRunnerId != runnerId
            || target.ActiveDeploymentOperationId != operationId
            || target.ActiveDeploymentExecutionAttemptId != attemptId)
            return null;
        try
        {
            return packageStore.OpenRead(operation.PackagePath);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public Task<Guid?> GetHostedRunnerIdAsync(Guid apiKeyId, CancellationToken cancellationToken)
        => db.Runners.AsNoTracking()
            .Where(runner => runner.ApiKeyId == apiKeyId && runner.Purpose == RunnerPurposes.Managed)
            .Select(runner => (Guid?)runner.Id)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<string> GetCommandDispositionAsync(
        Guid apiKeyId,
        Guid operationId,
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        var runnerId = await GetHostedRunnerIdAsync(apiKeyId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The API key is not assigned to a Hosted Runner.");
        var operation = await db.DeploymentOperations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
        if (operation is null)
            return "obsolete";
        if (operation.Status is DeploymentOperationStatuses.Succeeded or DeploymentOperationStatuses.Failed)
            return "already-applied";
        if (operation.Status != DeploymentOperationStatuses.Running)
            return "obsolete";

        var target = await ResolveTargetAsync(operation, cancellationToken);
        if (target?.ExecutionRunnerId != runnerId)
            return "obsolete";
        if (operation.ExecutionAttemptId != attemptId)
            return "stale";

        return target.ActiveDeploymentOperationId == operationId
            && target.ActiveDeploymentExecutionAttemptId == attemptId
                ? "execute"
                : "stale";
    }

    public Task<AppDeploymentTarget?> ResolveTargetAsync(
        DeploymentOperation operation,
        CancellationToken cancellationToken)
        => db.AppDeploymentTargets
            .Include(target => target.ExecutionRunner)
            .SingleOrDefaultAsync(target => target.WebAppId == operation.WebAppId
                && (operation.TargetSlug == null ? target.IsDefault : target.Slug == operation.TargetSlug),
                cancellationToken);

    private async Task EnsureRunnerCanDeployAsync(
        DeploymentOperation operation,
        Runner runner,
        CancellationToken cancellationToken)
    {
        RunnerProtocolCompatibilityPolicy.EnsureSupportsManagedApplicationArtifacts(runner);
        await using var package = packageStore.OpenRead(operation.PackagePath);
        var file = new Microsoft.AspNetCore.Http.FormFile(package, 0, package.Length, "file", "deploy-package.zip");
        var application = await DeployPackageContractService.ReadAsync(file, operation.WebAppId, cancellationToken);
        if (application.Runtime.Command is not null || application.Build.Command is not null)
            RunnerProtocolCompatibilityPolicy.EnsureSupportsContainerApplicationExecution(runner);
    }
}
