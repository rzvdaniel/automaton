using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Persists authorized Runner registrations without changing historical Hosted lifecycle records.
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

}
