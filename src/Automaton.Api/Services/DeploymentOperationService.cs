using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Messaging.Abstractions;
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
    DeploymentPackageStore packageStore,
    ApplicationDeploymentService deploymentService,
    ApiInstanceIdentityService apiInstanceIdentityService,
    IServiceScopeFactory scopeFactory,
    ILogger<DeploymentOperationService> logger)
{
    private readonly string executorInstanceId = apiInstanceIdentityService.ApiInstanceId;
    private static readonly TimeSpan ExecutionLeaseDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ExecutionLeaseRenewalInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UnreferencedPackageRetention = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

        if (!await authorizationService.CanUpdateAppAsync(appId, ownerId, cancellationToken))
            throw new KeyNotFoundException("App not found or you do not have permission to deploy it.");

        _ = await DeployPackageContractService.ReadAsync(file, appId, cancellationToken);

        var operationId = Guid.NewGuid();
        var packagePath = await packageStore.StoreAsync(operationId, file, cancellationToken);

        try
        {
            await new DeploymentOperationAdmission(dataContext, transactionalEventExecutor, eventPublisher)
                .SaveAsync(new DeploymentOperation
                {
                    Id = operationId, WebAppId = appId, OwnerUserId = ownerId,
                    TargetSlug = string.IsNullOrWhiteSpace(targetSlug) ? null : targetSlug.Trim(),
                    Status = DeploymentOperationStatuses.Pending, PackagePath = packagePath,
                    Clean = clean, CreatedAtUtc = DateTime.UtcNow
                }, cancellationToken);

            return new DeploymentOperationResponse
            {
                OperationId = operationId,
                Status = DeploymentOperationStatuses.Pending
            };
        }
        catch
        {
            packageStore.TryDelete(packagePath);
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
        if (!await IsTargetDispatchableAsync(operationId, cancellationToken))
        {
            logger.LogInformation(
                "Deferring deployment operation {OperationId}: deployment target is no longer dispatchable",
                operationId);
            return;
        }

        var executionAttemptId = Guid.NewGuid();
        var claim = await ClaimAsync(operationId, executionAttemptId, cancellationToken);
        if (claim == DeploymentOperationClaim.LiveLease)
            throw new DeploymentOperationLeaseActiveException(operationId);
        if (claim != DeploymentOperationClaim.Claimed)
            return;

        var operation = await dataContext.DeploymentOperations
            .AsNoTracking()
            .SingleAsync(item => item.Id == operationId, CancellationToken.None);
        using var leaseCancellation = new CancellationTokenSource();
        var leaseTask = RenewLeaseAsync(operationId, executionAttemptId, leaseCancellation.Token);

        try
        {
            await using var package = packageStore.OpenRead(operation.PackagePath);
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
        catch (DeploymentOperationRunnerOwnershipChangedException)
        {
            // Leave the durable lease running until expiry so the current
            // owner can reclaim the operation without a stale completion.
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
                packageStore.TryDelete(operation.PackagePath);
        }
    }

    internal async Task<List<Guid>> GetDispatchableOwnedOperationIdsAsync(
        int limit, IReadOnlyCollection<Guid> activeOperationIds, CancellationToken cancellationToken)
    {
        await CleanupUnreferencedPackagesAsync(cancellationToken);
        var now = DateTime.UtcNow;
        return await dataContext.DeploymentOperations
            .AsNoTracking()
            .Where(operation => !activeOperationIds.Contains(operation.Id))
            .Where(operation =>
                operation.Status == DeploymentOperationStatuses.Pending ||
                (operation.Status == DeploymentOperationStatuses.Running &&
                 operation.LeaseExpiresAtUtc != null && operation.LeaseExpiresAtUtc <= now))
            .Where(operation => dataContext.AppDeploymentTargets.Any(target =>
                target.WebAppId == operation.WebAppId &&
                (operation.TargetSlug == null
                    ? target.IsDefault
                    : target.Slug == operation.TargetSlug) &&
                (target.ExecutionRunnerId != null || operation.TargetSlug == null)))
            .OrderBy(operation => operation.CreatedAtUtc)
            .ThenBy(operation => operation.Id)
            .Select(operation => operation.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private async Task CleanupUnreferencedPackagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var activePackagePaths = await dataContext.DeploymentOperations
                .AsNoTracking()
                .Where(operation => operation.Status == DeploymentOperationStatuses.Pending ||
                    operation.Status == DeploymentOperationStatuses.Running)
                .Select(operation => operation.PackagePath)
                .ToListAsync(cancellationToken);
            var activeTransferIds = await dataContext.RunnerExecutionOperations
                .AsNoTracking()
                .Where(operation => operation.CompletedAtUtc == null)
                .Select(operation => operation.Id)
                .ToListAsync(cancellationToken);
            activePackagePaths.AddRange(activeTransferIds.SelectMany(id => new[]
            {
                packageStore.GetRunnerTransferPath(id, input: true),
                packageStore.GetRunnerTransferPath(id, input: false)
            }));

            var deletedCount = await packageStore.CleanupUnreferencedPackagesAsync(
                activePackagePaths,
                UnreferencedPackageRetention,
                cancellationToken);
            if (deletedCount > 0)
                logger.LogInformation("Removed {PackageCount} stale deployment package files", deletedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not clean stale deployment package files");
        }
    }

    private async Task<bool> IsTargetDispatchableAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var operation = await dataContext.DeploymentOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
        if (operation is null)
            return false;

        return await dataContext.AppDeploymentTargets
            .AnyAsync(target =>
            target.WebAppId == operation.WebAppId &&
            (operation.TargetSlug == null
                ? target.IsDefault
                : target.Slug == operation.TargetSlug) &&
            (target.ExecutionRunnerId != null || operation.TargetSlug == null),
            cancellationToken);
    }

    private async Task<DeploymentOperationClaim> ClaimAsync(
        Guid operationId,
        Guid executionAttemptId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var leaseExpiresAt = now.Add(ExecutionLeaseDuration);
        if (dataContext.Database.IsRelational())
        {
            var claimed = await dataContext.DeploymentOperations
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
                    .SetProperty(item => item.ErrorMessage, (string?)null)
                    .SetProperty(item => item.ResultJson, (string?)null)
                    .SetProperty(item => item.LeaseExpiresAtUtc, leaseExpiresAt), cancellationToken) == 1;
            if (claimed)
                return DeploymentOperationClaim.Claimed;

            var current = await dataContext.DeploymentOperations
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
            return GetUnclaimedOperationResult(current, now);
        }

        var operation = await dataContext.DeploymentOperations
            .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
        if (operation is null)
            return DeploymentOperationClaim.Missing;
        if (operation.Status != DeploymentOperationStatuses.Pending &&
            operation.Status != DeploymentOperationStatuses.Running)
            return DeploymentOperationClaim.Terminal;
        if (operation.Status == DeploymentOperationStatuses.Running &&
            operation.LeaseExpiresAtUtc is not null && operation.LeaseExpiresAtUtc > now)
            return DeploymentOperationClaim.LiveLease;
        operation.Status = DeploymentOperationStatuses.Running;
        operation.StartedAtUtc = now;
        operation.ExecutorInstanceId = executorInstanceId;
        operation.ExecutionAttemptId = executionAttemptId;
        operation.ErrorMessage = null;
        operation.ResultJson = null;
        operation.LeaseExpiresAtUtc = leaseExpiresAt;
        await dataContext.SaveChangesAsync(cancellationToken);
        return DeploymentOperationClaim.Claimed;
    }

    private static DeploymentOperationClaim GetUnclaimedOperationResult(
        DeploymentOperation? operation,
        DateTime now)
    {
        if (operation is null)
            return DeploymentOperationClaim.Missing;
        if (operation.Status != DeploymentOperationStatuses.Pending &&
            operation.Status != DeploymentOperationStatuses.Running)
            return DeploymentOperationClaim.Terminal;
        if (operation.Status == DeploymentOperationStatuses.Running &&
            operation.LeaseExpiresAtUtc is not null && operation.LeaseExpiresAtUtc > now)
            return DeploymentOperationClaim.LiveLease;

        // A concurrent writer changed the row between the conditional claim
        // and this read. Treat it as retryable so CAP does not acknowledge a
        // delivery that still needs an eventual claim.
        return DeploymentOperationClaim.LiveLease;
    }

    private enum DeploymentOperationClaim
    {
        Claimed,
        LiveLease,
        Terminal,
        Missing
    }

    private async Task RenewLeaseAsync(
        Guid operationId,
        Guid executionAttemptId,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
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
        if (operation.Status is DeploymentOperationStatuses.Succeeded or DeploymentOperationStatuses.Failed
            && !string.IsNullOrWhiteSpace(operation.ResultJson))
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

}
