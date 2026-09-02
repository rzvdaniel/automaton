using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

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
    ApiInstanceIdentityService apiInstanceIdentityService)
{
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
        var claimed = await ClaimAsync(operationId, cancellationToken);
        if (!claimed)
            return;

        var operation = await dataContext.DeploymentOperations
            .AsNoTracking()
            .SingleAsync(item => item.Id == operationId, cancellationToken);

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
                cancellationToken,
                operation.Id);

            await CompleteAsync(operationId, result.Success, result, result.Success ? null : result.Message, cancellationToken);
        }
        catch (Exception exception)
        {
            var result = new DeployPackageResponse
            {
                Success = false,
                State = DeploymentOperationStatuses.Failed,
                Message = exception.Message
            };
            await CompleteAsync(operationId, false, result, exception.Message, CancellationToken.None);
        }
        finally
        {
            var current = await dataContext.DeploymentOperations
                .AsNoTracking()
                .Where(item => item.Id == operationId)
                .Select(item => item.Status)
                .SingleOrDefaultAsync(CancellationToken.None);
            if (current is DeploymentOperationStatuses.Succeeded or DeploymentOperationStatuses.Failed)
                TryDeletePackage(operation.PackagePath);
        }
    }

    private async Task<bool> ClaimAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (dataContext.Database.IsRelational())
        {
            return await dataContext.DeploymentOperations
                .Where(item =>
                    item.Id == operationId &&
                    (item.Status == DeploymentOperationStatuses.Pending ||
                     (item.Status == DeploymentOperationStatuses.Running &&
                      (item.ExecutorInstanceId == null || item.ExecutorInstanceId != executorInstanceId))))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, DeploymentOperationStatuses.Running)
                    .SetProperty(item => item.StartedAtUtc, now)
                    .SetProperty(item => item.ExecutorInstanceId, executorInstanceId), cancellationToken) == 1;
        }

        var operation = await dataContext.DeploymentOperations
            .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
        if (operation is null ||
            (operation.Status != DeploymentOperationStatuses.Pending &&
             (operation.Status != DeploymentOperationStatuses.Running ||
              operation.ExecutorInstanceId == executorInstanceId)))
            return false;
        operation.Status = DeploymentOperationStatuses.Running;
        operation.StartedAtUtc = now;
        operation.ExecutorInstanceId = executorInstanceId;
        await dataContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task CompleteAsync(
        Guid operationId,
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
                    item.ExecutorInstanceId == executorInstanceId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, status)
                    .SetProperty(item => item.CompletedAtUtc, DateTime.UtcNow)
                    .SetProperty(item => item.ErrorMessage, error)
                    .SetProperty(item => item.ResultJson, resultJson), cancellationToken);
            return;
        }

        var operation = await dataContext.DeploymentOperations
            .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
        if (operation is null ||
            operation.Status != DeploymentOperationStatuses.Running ||
            operation.ExecutorInstanceId != executorInstanceId)
            return;
        operation.Status = status;
        operation.CompletedAtUtc = DateTime.UtcNow;
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
