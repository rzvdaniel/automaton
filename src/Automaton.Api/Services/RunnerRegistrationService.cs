using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Persists Runner registrations and completes the associated Hosted Runner enrollment lifecycle.
/// </summary>
public sealed class RunnerRegistrationService(
    ApplicationDbContext dbContext,
    UserContextService userContextService,
    RunnerAuditService runnerAuditService)
{
    private readonly Serilog.ILogger logger = Serilog.Log.ForContext<RunnerRegistrationService>();
    private Guid UserId => userContextService.GetUserId();

    public async Task<int> Add(RunnerDetails runnerModel, CancellationToken cancellationToken)
    {
        logger.Debug("Adding runner {RunnerId} ({RunnerName}) for user {UserId}", runnerModel.Id, runnerModel.Name, UserId);

        if (UserId == Guid.Empty)
        {
            logger.Error("Cannot add runner: userId is empty/null. User context not properly set from authentication");
            logger.Error("This usually means API key authentication claims are not being properly converted to user context");
            throw new InvalidOperationException("User context not available. Authentication may not have been properly processed. UserId is empty.");
        }

        var purpose = RunnerPurposes.Normalize(runnerModel.Purpose);
        var capabilities = RunnerCapabilities.Normalize(runnerModel.Capabilities, purpose);
        if (RunnerCapabilities.RequiresAdministrator(capabilities) && !await IsAdministratorAsync(UserId, cancellationToken))
            throw new UnauthorizedAccessException("Only an administrator may register a Runner with privileged capabilities.");

        var runner = new Runner
        {
            Id = runnerModel.Id,
            Name = runnerModel.Name,
            Status = RunnerConnectivityStatuses.Unknown,
            Version = runnerModel.Version,
            MachineName = runnerModel.MachineName,
            IpAddress = runnerModel.IpAddress,
            Purpose = purpose,
            CapabilitiesJson = RunnerCapabilities.Serialize(capabilities, purpose),
            InstallationId = runnerModel.InstallationId
        };
        dbContext.Runners.Add(runner);

        dbContext.RunnerUsers.Add(new RunnerUser
        {
            RunnerId = runner.Id,
            UserId = UserId
        });

        if (capabilities.Contains(RunnerCapabilities.HostedRunnerProvisioning, StringComparer.OrdinalIgnoreCase) &&
            runnerModel.RunnerHostId.HasValue)
        {
            var host = await dbContext.RunnerHosts
                .FirstOrDefaultAsync(x => x.Id == runnerModel.RunnerHostId.Value, cancellationToken);
            if (host == null)
                throw new InvalidOperationException("The infrastructure runner host was not found.");

            if (host.InfrastructureRunnerId.HasValue && host.InfrastructureRunnerId != runner.Id)
                throw new InvalidOperationException("The runner host is already linked to another infrastructure runner.");

            host.InfrastructureRunnerId = runner.Id;
            host.UpdatedAt = DateTime.UtcNow;
        }

        var result = await dbContext.SaveChangesAsync(cancellationToken);
        var completedHostedRunnerAction = LinkHostedRunnerAfterRegistration(runner, runnerModel.HostedRunnerId);
        result += await dbContext.SaveChangesAsync(cancellationToken);
        await WriteHostedRunnerRegistrationCompletionAuditAsync(completedHostedRunnerAction, runner, cancellationToken);

        await runnerAuditService.WriteCreatedAsync(runner, UserId, runnerModel.RunnerHostId, result, cancellationToken);

        logger.Debug("Runner created successfully: ID={RunnerId}", runner.Id);
        return result;
    }

    public async Task<bool> IsAdministratorAsync(Guid userId, CancellationToken cancellationToken)
        => await (from userRole in dbContext.UserRoles
                  join role in dbContext.Roles on userRole.RoleId equals role.Id
                  where userRole.UserId == userId
                      && (role.Name == UserRole.Administrator || role.Name == UserRole.SystemAdministrator)
                  select userRole).AnyAsync(cancellationToken);

    public async Task CompleteReconnectAsync(
        Runner runner,
        Guid userId,
        Guid? hostedRunnerId,
        CancellationToken cancellationToken)
    {
        var completedHostedRunnerAction = LinkHostedRunnerAfterReconnect(runner, userId, hostedRunnerId);
        await dbContext.SaveChangesAsync(cancellationToken);
        await WriteHostedRunnerRegistrationCompletionAuditAsync(completedHostedRunnerAction, runner, cancellationToken);
    }

    private string? LinkHostedRunnerAfterRegistration(Runner runner, Guid? hostedRunnerId)
    {
        var hostedRunner = dbContext.HostedRunners
            .Include(x => x.Deployments)
            .FirstOrDefault(x =>
                x.UserId == UserId &&
                (hostedRunnerId.HasValue ? x.Id == hostedRunnerId.Value : x.Name == runner.Name) &&
                (x.RunnerId == null || x.RunnerId == runner.Id) &&
                x.DeletedAt == null &&
                (x.Status == HostedRunnerStatuses.Pending
                    || x.Status == HostedRunnerStatuses.Provisioning
                    || x.Status == HostedRunnerStatuses.Reprovisioning));

        if (hostedRunner == null || !IsRegistrationAttemptCurrent(runner, hostedRunner))
            return null;

        hostedRunner.RunnerId = runner.Id;
        hostedRunner.Status = HostedRunnerStatuses.Running;
        hostedRunner.ProvisionedAt ??= DateTime.UtcNow;
        hostedRunner.UpdatedAt = DateTime.UtcNow;
        ApplyPendingHostedRunnerSpecification(hostedRunner);
        var deploymentAction = CompleteLatestRegistrationDeployment(
            hostedRunner,
            includeSucceeded: false,
            "Runner registered successfully and is linked to this hosted runner.");
        logger.Debug("Hosted runner {HostedRunnerId} linked to registered runner {RunnerId}", hostedRunner.Id, runner.Id);
        return deploymentAction;
    }

    private string? LinkHostedRunnerAfterReconnect(Runner runner, Guid userId, Guid? hostedRunnerId)
    {
        var hostedRunner = dbContext.HostedRunners
            .Include(x => x.Deployments)
            .FirstOrDefault(x =>
                x.UserId == userId &&
                (hostedRunnerId.HasValue ? x.Id == hostedRunnerId.Value : x.Name == runner.Name) &&
                (x.RunnerId == null || x.RunnerId == runner.Id) &&
                x.DeletedAt == null &&
                (x.Status == HostedRunnerStatuses.Pending
                    || x.Status == HostedRunnerStatuses.Provisioning
                    || x.Status == HostedRunnerStatuses.Reprovisioning
                    || x.Status == HostedRunnerStatuses.Failed));

        if (hostedRunner == null || !IsRegistrationAttemptCurrent(runner, hostedRunner))
            return null;

        hostedRunner.RunnerId = runner.Id;
        hostedRunner.Status = HostedRunnerStatuses.Running;
        hostedRunner.ProvisionedAt ??= DateTime.UtcNow;
        hostedRunner.UpdatedAt = DateTime.UtcNow;
        hostedRunner.LastError = null;
        ApplyPendingHostedRunnerSpecification(hostedRunner);
        var deploymentAction = CompleteLatestRegistrationDeployment(
            hostedRunner,
            includeSucceeded: true,
            "Runner connected successfully and is linked to this hosted runner.");
        CompleteEnforcementResume(hostedRunner);
        logger.Warning("Hosted runner {HostedRunnerId} re-linked to runner {RunnerId} after reconnect", hostedRunner.Id, runner.Id);
        return deploymentAction;
    }

    private static string? CompleteLatestRegistrationDeployment(
        HostedRunner hostedRunner,
        bool includeSucceeded,
        string completionMessage)
    {
        var deployment = hostedRunner.Deployments
            .Where(deployment =>
                (deployment.Action == HostedRunnerDeploymentActions.Provision ||
                 deployment.Action == HostedRunnerDeploymentActions.Reprovision) &&
                (deployment.Status == HostedRunnerDeploymentStatuses.WaitingForRegistration ||
                 includeSucceeded && deployment.Status == HostedRunnerDeploymentStatuses.Succeeded))
            .OrderByDescending(deployment => deployment.StartedAt)
            .FirstOrDefault();
        if (deployment is null)
            return null;

        deployment.Status = HostedRunnerDeploymentStatuses.Succeeded;
        deployment.FinishedAt = DateTime.UtcNow;
        deployment.Logs = completionMessage;
        if (includeSucceeded)
            deployment.Error = null;
        return deployment.Action;
    }

    private void CompleteEnforcementResume(HostedRunner hostedRunner)
    {
        var enforcementResume = hostedRunner.Deployments
            .Where(deployment => deployment.Action == HostedRunnerDeploymentActions.Resume &&
                deployment.Status == HostedRunnerDeploymentStatuses.WaitingForReconnect)
            .OrderByDescending(deployment => deployment.StartedAt)
            .FirstOrDefault();
        if (enforcementResume is null)
            return;

        enforcementResume.Status = HostedRunnerDeploymentStatuses.Succeeded;
        enforcementResume.FinishedAt = DateTime.UtcNow;
        enforcementResume.Logs = "Hosted Runner reconnected after storage enforcement recovery.";
        hostedRunner.EnforcementState = HostedRunnerEnforcementStates.Recovered;
        hostedRunner.RecoveredAtUtc = DateTime.UtcNow;
        hostedRunner.LastEnforcementReason = "Runner reconnected after authoritative storage recovery.";
        dbContext.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(), Category = "Audit", Action = "Changed", EntityType = "HostedRunner", EntityId = hostedRunner.Id,
            TargetUserId = hostedRunner.UserId, Outcome = "Success", EventType = StorageTelemetryAuditEvents.EnforcementRecovered,
            Message = "Hosted Runner recovered after storage enforcement.", CreatedAtUtc = DateTime.UtcNow
        });
    }

    private bool IsRegistrationAttemptCurrent(Runner runner, HostedRunner hostedRunner)
    {
        var operationId = dbContext.RunnerEnrollments
            .Where(x => x.HostedRunnerId == hostedRunner.Id && x.RunnerId == runner.Id && x.OperationId != null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.OperationId)
            .FirstOrDefault();
        if (!operationId.HasValue)
            return true;

        var operation = hostedRunner.Deployments.FirstOrDefault(x => x.Id == operationId.Value);
        var isCurrent = operation != null
            && operation.Status is HostedRunnerDeploymentStatuses.WaitingForRegistration or HostedRunnerDeploymentStatuses.Succeeded
            && (operation.Status == HostedRunnerDeploymentStatuses.Succeeded
                || operation.RegistrationDeadlineUtc > DateTime.UtcNow);
        if (!isCurrent)
            logger.Warning("Rejected Runner {RunnerId} registration for stale hosted Runner operation {OperationId}", runner.Id, operationId);
        return isCurrent;
    }

    private async Task WriteHostedRunnerRegistrationCompletionAuditAsync(
        string? action,
        Runner runner,
        CancellationToken cancellationToken)
    {
        if (action is not (HostedRunnerDeploymentActions.Provision or HostedRunnerDeploymentActions.Reprovision))
            return;
        var hostedRunner = await dbContext.HostedRunners.AsNoTracking()
            .SingleAsync(x => x.RunnerId == runner.Id && x.DeletedAt == null, cancellationToken);
        await runnerAuditService.WriteHostedRunnerRegistrationCompletedAsync(hostedRunner, runner, action, cancellationToken);
    }

    private static void ApplyPendingHostedRunnerSpecification(HostedRunner hostedRunner)
    {
        if (hostedRunner.PendingSpecificationSnapshot == null)
            return;
        hostedRunner.SpecificationSnapshot = hostedRunner.PendingSpecificationSnapshot;
        hostedRunner.HostedRunnerSpecificationId = hostedRunner.SpecificationSnapshot.SourceSpecificationId;
        hostedRunner.SpecificationRevision = hostedRunner.SpecificationSnapshot.SourceSpecificationRevision;
        hostedRunner.PendingSpecificationSnapshot = null;
    }
}
