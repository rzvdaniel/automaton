using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Automaton.Api.Services;

/// <summary>
/// Accepts package deployments independently of the HTTP request lifetime and
/// executes them from durable package/operation state.
/// </summary>
public sealed class DeploymentOperationService(
    ApplicationDbContext dataContext,
    ResourceAuthorizationService authorizationService,
    ITransactionalEventExecutor transactionalEventExecutor,
    IEventPublisher eventPublisher,
    IWebHostEnvironment webHostEnvironment,
    AppsControllerExecutionService deploymentService,
    ApiInstanceIdentityService apiInstanceIdentityService,
    IServiceScopeFactory scopeFactory,
    ILogger<DeploymentOperationService> logger)
{
    private static readonly TimeSpan ExecutionLeaseDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ExecutionLeaseRenewalInterval = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string executorInstanceId = apiInstanceIdentityService.ApiInstanceId;

    public async Task<DeploymentOperationResponse> AcceptAsync(
        Guid appId,
        Guid ownerId,
        string? targetSlug,
        IFormFile? file,
        bool clean,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length <= 0)
            throw new InvalidOperationException("A deployment package file is required.");

        if (!await authorizationService.CanAccessAppAsync(appId, ownerId, cancellationToken))
            throw new KeyNotFoundException("App not found.");

        _ = await DeployPackageContractService.ReadAsync(file, appId, cancellationToken);

        var operationId = Guid.NewGuid();
        var packageDirectory = GetPackageDirectory();
        Directory.CreateDirectory(packageDirectory);
        var packagePath = Path.Combine(packageDirectory, $"{operationId:N}.zip");

        try
        {
            await using (var package = new FileStream(
                packagePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                useAsync: true))
            {
                await file.CopyToAsync(package, cancellationToken);
            }

            await transactionalEventExecutor.ExecuteAsync(async transactionCancellationToken =>
            {
                dataContext.DeploymentOperations.Add(new DeploymentOperation
                {
                    Id = operationId,
                    WebAppId = appId,
                    OwnerUserId = ownerId,
                    TargetSlug = string.IsNullOrWhiteSpace(targetSlug) ? null : targetSlug.Trim(),
                    Status = DeploymentOperationStatuses.Pending,
                    PackagePath = packagePath,
                    Clean = clean,
                    CreatedAtUtc = DateTime.UtcNow
                });
                await dataContext.SaveChangesAsync(transactionCancellationToken);
                await eventPublisher.PublishAsync(
                    new DeploymentPackageRequestedEvent { OperationId = operationId },
                    transactionCancellationToken);
            }, cancellationToken);

            return new DeploymentOperationResponse
            {
                OperationId = operationId,
                Status = DeploymentOperationStatuses.Pending
            };
        }
        catch
        {
            TryDeletePackage(packagePath);
            throw;
        }
    }

    public async Task<DeploymentOperationResponse?> GetAsync(
        Guid appId,
        Guid ownerId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (!await authorizationService.CanAccessAppAsync(appId, ownerId, cancellationToken))
            return null;

        var operation = await dataContext.DeploymentOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == operationId && item.WebAppId == appId && item.OwnerUserId == ownerId,
                cancellationToken);
        return operation is null ? null : ToResponse(operation);
    }

    public async Task ExecuteAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var executionAttemptId = Guid.NewGuid();
        var claimed = await ClaimAsync(operationId, executionAttemptId, cancellationToken);
        if (!claimed)
            return;

        var operation = await dataContext.DeploymentOperations
            .AsNoTracking()
            .SingleAsync(item => item.Id == operationId, CancellationToken.None);
        using var leaseCancellation = new CancellationTokenSource();
        var leaseTask = RenewLeaseAsync(operationId, executionAttemptId, leaseCancellation.Token);

        try
        {
            await using var package = new FileStream(
                operation.PackagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                useAsync: true);
            var file = new FormFile(package, 0, package.Length, "file", "deploy-package.zip");
            var result = await deploymentService.DeployPackageAsync(
                operation.WebAppId,
                operation.OwnerUserId,
                operation.TargetSlug,
                file,
                operation.Clean,
                CancellationToken.None,
                operation.Id,
                executionAttemptId);

            await CompleteAsync(
                operationId,
                executionAttemptId,
                result.Success,
                result,
                result.Success ? null : result.Message,
                CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation of the CAP/API wait does not establish that the
            // Runner stopped. Leave the operation and staged package durable
            // for retry/recovery rather than converting it to Failed.
            throw;
        }
        catch (Exception exception)
        {
            var result = new DeployPackageResponse
            {
                Success = false,
                State = DeploymentOperationStatuses.Failed,
                Message = exception.Message
            };
            await CompleteAsync(
                operationId,
                executionAttemptId,
                false,
                result,
                exception.Message,
                CancellationToken.None);
        }
        finally
        {
            leaseCancellation.Cancel();
            try
            {
                await leaseTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Deployment execution lease renewal stopped for operation {OperationId}",
                    operationId);
            }

            var current = await dataContext.DeploymentOperations
                .AsNoTracking()
                .Where(item => item.Id == operationId)
                .Select(item => item.Status)
                .SingleOrDefaultAsync(CancellationToken.None);
            if (current is DeploymentOperationStatuses.Succeeded or DeploymentOperationStatuses.Failed)
                TryDeletePackage(operation.PackagePath);
        }
    }

    private async Task<bool> ClaimAsync(
        Guid operationId,
        Guid executionAttemptId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var leaseExpiresAt = now.Add(ExecutionLeaseDuration);
        if (dataContext.Database.IsRelational())
        {
            return await dataContext.DeploymentOperations
                .Where(item =>
                    item.Id == operationId &&
                    (item.Status == DeploymentOperationStatuses.Pending ||
                     (item.Status == DeploymentOperationStatuses.Running &&
                      (item.LeaseExpiresAtUtc == null || item.LeaseExpiresAtUtc <= now))))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, DeploymentOperationStatuses.Running)
                    .SetProperty(item => item.StartedAtUtc, now)
                    .SetProperty(item => item.ExecutorInstanceId, executorInstanceId)
                    .SetProperty(item => item.ExecutionAttemptId, executionAttemptId)
                    .SetProperty(item => item.LeaseExpiresAtUtc, leaseExpiresAt), cancellationToken) == 1;
        }

        var operation = await dataContext.DeploymentOperations
            .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
        if (operation is null ||
            (operation.Status != DeploymentOperationStatuses.Pending &&
             (operation.Status != DeploymentOperationStatuses.Running ||
              operation.LeaseExpiresAtUtc is not null && operation.LeaseExpiresAtUtc > now)))
            return false;
        operation.Status = DeploymentOperationStatuses.Running;
        operation.StartedAtUtc = now;
        operation.ExecutorInstanceId = executorInstanceId;
        operation.ExecutionAttemptId = executionAttemptId;
        operation.LeaseExpiresAtUtc = leaseExpiresAt;
        await dataContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task RenewLeaseAsync(
        Guid operationId,
        Guid executionAttemptId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(ExecutionLeaseRenewalInterval, cancellationToken);
            var leaseExpiresAt = DateTime.UtcNow.Add(ExecutionLeaseDuration);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var leaseContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await leaseContext.DeploymentOperations
                    .Where(item =>
                        item.Id == operationId &&
                        item.Status == DeploymentOperationStatuses.Running &&
                        item.ExecutionAttemptId == executionAttemptId)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(item => item.LeaseExpiresAtUtc, leaseExpiresAt),
                        CancellationToken.None);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to renew deployment execution lease for operation {OperationId}",
                    operationId);
            }
        }
    }

    private async Task CompleteAsync(
        Guid operationId,
        Guid executionAttemptId,
        bool success,
        DeployPackageResponse result,
        string? error,
        CancellationToken cancellationToken)
    {
        var status = success ? DeploymentOperationStatuses.Succeeded : DeploymentOperationStatuses.Failed;
        var resultJson = JsonSerializer.Serialize(result, JsonOptions);
        if (dataContext.Database.IsRelational())
        {
            await dataContext.DeploymentOperations
                .Where(item =>
                    item.Id == operationId &&
                    item.Status == DeploymentOperationStatuses.Running &&
                    item.ExecutionAttemptId == executionAttemptId &&
                    item.ExecutorInstanceId == executorInstanceId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, status)
                    .SetProperty(item => item.CompletedAtUtc, DateTime.UtcNow)
                    .SetProperty(item => item.LeaseExpiresAtUtc, (DateTime?)null)
                    .SetProperty(item => item.ErrorMessage, error)
                    .SetProperty(item => item.ResultJson, resultJson), cancellationToken);
            return;
        }

        var operation = await dataContext.DeploymentOperations
            .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
        if (operation is null ||
            operation.Status != DeploymentOperationStatuses.Running ||
            operation.ExecutionAttemptId != executionAttemptId ||
            operation.ExecutorInstanceId != executorInstanceId)
            return;
        operation.Status = status;
        operation.CompletedAtUtc = DateTime.UtcNow;
        operation.LeaseExpiresAtUtc = null;
        operation.ErrorMessage = error;
        operation.ResultJson = resultJson;
        await dataContext.SaveChangesAsync(cancellationToken);
    }

    private DeploymentOperationResponse ToResponse(DeploymentOperation operation)
    {
        DeployPackageResponse? result = null;
        if (!string.IsNullOrWhiteSpace(operation.ResultJson))
        {
            result = JsonSerializer.Deserialize<DeployPackageResponse>(operation.ResultJson, JsonOptions);
        }

        return new DeploymentOperationResponse
        {
            OperationId = operation.Id,
            Status = operation.Status,
            Success = result?.Success,
            Error = operation.ErrorMessage,
            Result = result
        };
    }

    private string GetPackageDirectory() =>
        Path.Combine(webHostEnvironment.ContentRootPath, "AppData", "deployment-operations");

    private static void TryDeletePackage(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Package cleanup is best effort and must not change operation state.
        }
    }
}
