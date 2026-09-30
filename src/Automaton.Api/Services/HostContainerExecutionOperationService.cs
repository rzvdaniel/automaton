using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class HostContainerExecutionOperationService(
    ApplicationDbContext db,
    IEventPublisher events)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<HostContainerExecutionOperationResponse> QueueAsync(
        HostContainerExecutionRequest request,
        Guid runnerHostId,
        Guid infrastructureRunnerId,
        CancellationToken cancellationToken)
    {
        var requestPayload = JsonSerializer.Serialize(request, JsonOptions);
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestPayload)));
        var operation = await db.HostContainerExecutionOperations.SingleOrDefaultAsync(
            item => item.Id == request.OperationId, cancellationToken);
        if (operation is not null && operation.RequestHash != requestHash)
            return Conflict(request.OperationId);
        if (operation is not null && (operation.RunnerHostId != runnerHostId
            || operation.InfrastructureRunnerId != infrastructureRunnerId))
            return Conflict(request.OperationId);

        if (operation is null)
        {
            operation = CreateOperation(request, runnerHostId, infrastructureRunnerId, requestHash);
            db.HostContainerExecutionOperations.Add(operation);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                operation = await db.HostContainerExecutionOperations.SingleOrDefaultAsync(
                    item => item.Id == request.OperationId, cancellationToken);
                if (operation is null || operation.RequestHash != requestHash)
                    throw;
            }
        }

        if (operation.RunnerHostId != runnerHostId || operation.InfrastructureRunnerId != infrastructureRunnerId)
            return Conflict(request.OperationId);

        if (operation.ResultJson is not null)
            return ReadCompleted(operation);

        await PublishAsync(request, runnerHostId, infrastructureRunnerId, cancellationToken);
        return new(operation.Id, false, null);
    }

    public async Task<HostContainerExecutionOperationResponse?> GetResultAsync(
        Guid apiKeyId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var runner = await GetRunnerForApiKeyAsync(apiKeyId, RunnerPurposes.Managed, cancellationToken);
        if (runner is null)
            return null;
        var hostedRunnerId = await db.HostedRunners.AsNoTracking()
            .Where(hosted => hosted.RunnerId == runner.Id && hosted.DeletedAt == null)
            .Select(hosted => (Guid?)hosted.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (hostedRunnerId is null)
            return null;
        var operation = await db.HostContainerExecutionOperations.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == operationId && item.HostedRunnerId == hostedRunnerId.Value, cancellationToken);
        return operation is null ? null : ToResponse(operation);
    }

    public async Task<string> GetCommandDispositionAsync(
        Guid apiKeyId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var runner = await GetRunnerForApiKeyAsync(apiKeyId, RunnerPurposes.Infrastructure, cancellationToken)
            ?? throw new UnauthorizedAccessException("The API key is not assigned to an Infrastructure Runner.");
        var operation = await db.HostContainerExecutionOperations.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == operationId && item.InfrastructureRunnerId == runner.Id, cancellationToken);
        if (operation is null) return "obsolete";
        if (operation.ResultJson is not null) return "already-applied";
        if (await HasCurrentAssignmentAsync(operation, cancellationToken)) return "execute";

        var outcome = await ApplyResultAsync(apiKeyId, operationId, new(
            operation.RunnerHostId, operation.InfrastructureRunnerId,
            new HostContainerExecutionResponse
            {
                ErrorCode = "operation_obsolete",
                Error = "The host-container assignment or deployment attempt is no longer current."
            }), cancellationToken);
        return outcome == "conflict" ? "already-applied" : "obsolete";
    }

    private Task<bool> HasCurrentAssignmentAsync(
        HostContainerExecutionOperation operation,
        CancellationToken cancellationToken)
        => db.HostedRunners.AsNoTracking().AnyAsync(hosted =>
            hosted.Id == operation.HostedRunnerId && hosted.DeletedAt == null
            && hosted.RunnerId != null && hosted.RunnerHostId == operation.RunnerHostId
            && db.RunnerHosts.Any(host => host.Id == operation.RunnerHostId
                && host.InfrastructureRunnerId == operation.InfrastructureRunnerId)
            && db.AppDeploymentTargets.Any(target => target.Id == operation.DeploymentTargetId
                && target.WebAppId == operation.AppId && target.ExecutionRunnerId == hosted.RunnerId
                && (operation.DeploymentAttemptId == Guid.Empty
                    || target.ActiveDeploymentExecutionAttemptId == operation.DeploymentAttemptId)), cancellationToken);

    public async Task<string> ApplyResultAsync(
        Guid apiKeyId,
        Guid operationId,
        HostContainerExecutionResultRequest request,
        CancellationToken cancellationToken)
    {
        var runner = await GetRunnerForApiKeyAsync(apiKeyId, RunnerPurposes.Infrastructure, cancellationToken)
            ?? throw new UnauthorizedAccessException("The API key is not assigned to an Infrastructure Runner.");
        var operation = await db.HostContainerExecutionOperations.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == operationId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The host-container operation was not found.");
        if (request.RunnerHostId != operation.RunnerHostId
            || request.InfrastructureRunnerId != runner.Id
            || operation.InfrastructureRunnerId != runner.Id)
            throw new UnauthorizedAccessException("The host-container result is not assigned to this Infrastructure Runner.");

        var resultJson = JsonSerializer.Serialize(request.Result, JsonOptions);
        if (operation.ResultJson is not null)
            return operation.ResultJson == resultJson ? "already-applied" : "conflict";

        var completedAtUtc = DateTime.UtcNow;
        var status = request.Result.Success
            ? HostContainerExecutionOperationStatuses.Succeeded
            : HostContainerExecutionOperationStatuses.Failed;
        if (!db.Database.IsRelational())
        {
            var tracked = await db.HostContainerExecutionOperations.SingleAsync(
                item => item.Id == operationId, cancellationToken);
            tracked.ResultJson = resultJson;
            tracked.Status = status;
            tracked.CompletedAtUtc = completedAtUtc;
            await db.SaveChangesAsync(cancellationToken);
            return "accepted";
        }

        var updated = await db.HostContainerExecutionOperations
            .Where(item => item.Id == operationId && item.ResultJson == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ResultJson, resultJson)
                .SetProperty(item => item.Status, status)
                .SetProperty(item => item.CompletedAtUtc, completedAtUtc), cancellationToken);
        if (updated == 1)
            return "accepted";

        var persisted = await db.HostContainerExecutionOperations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
        return persisted?.ResultJson == resultJson ? "already-applied" : "conflict";
    }

    private Task<Runner?> GetRunnerForApiKeyAsync(
        Guid apiKeyId,
        string purpose,
        CancellationToken cancellationToken)
        => db.Runners.SingleOrDefaultAsync(runner => runner.ApiKeyId == apiKeyId
            && runner.Purpose == purpose, cancellationToken);

    private Task PublishAsync(
        HostContainerExecutionRequest request,
        Guid runnerHostId,
        Guid infrastructureRunnerId,
        CancellationToken cancellationToken)
        => events.PublishAsync(new InfrastructureCommandRequestedEvent(
            request.OperationId,
            request.OperationId,
            runnerHostId,
            infrastructureRunnerId,
            HostContainerExecutionCommandKinds.InfrastructureActionKey,
            JsonSerializer.Serialize(request, JsonOptions),
            [],
            DateTime.UtcNow), cancellationToken);

    private static HostContainerExecutionOperation CreateOperation(
        HostContainerExecutionRequest request,
        Guid runnerHostId,
        Guid infrastructureRunnerId,
        string requestHash)
        => new()
        {
            Id = request.OperationId,
            HostedRunnerId = request.Scope.HostedRunnerId,
            RunnerHostId = runnerHostId,
            InfrastructureRunnerId = infrastructureRunnerId,
            AppId = request.Scope.AppId,
            DeploymentTargetId = request.Scope.DeploymentTargetId,
            DeploymentAttemptId = request.Scope.DeploymentAttemptId,
            RequestHash = requestHash
        };

    private static HostContainerExecutionOperationResponse ToResponse(HostContainerExecutionOperation operation)
        => operation.ResultJson is null
            ? new(operation.Id, false, null)
            : ReadCompleted(operation);

    private static HostContainerExecutionOperationResponse ReadCompleted(HostContainerExecutionOperation operation)
        => new(operation.Id, true, JsonSerializer.Deserialize<HostContainerExecutionResponse>(operation.ResultJson!, JsonOptions)
            ?? throw new InvalidDataException("The saved host-container operation result is invalid."));

    private static HostContainerExecutionOperationResponse Conflict(Guid operationId)
        => new(operationId, true, new HostContainerExecutionResponse
        {
            ErrorCode = "operation_conflict",
            Error = "The operation ID was already used for a different host-container request."
        });
}
