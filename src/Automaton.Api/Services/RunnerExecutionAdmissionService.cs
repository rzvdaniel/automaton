using Automaton.Api.Data;
using Automaton.Api.Enums;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class RunnerExecutionAdmissionService(
    ApplicationDbContext db, ResourceAuthorizationService authorization, UserContextService userContext)
{
    public async Task<Guid> AuthorizeAsync(Guid runnerId, Guid? userId, CancellationToken token)
    {
        var actor = userId ?? userContext.GetUserId();
        var query = userId.HasValue
            ? db.Runners.Where(r => r.RunnerUsers.Any(u => u.UserId == actor))
            : authorization.GetAccessibleRunnersQuery();
        var runner = await query.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runnerId, token)
            ?? throw new UnauthorizedAccessException("Runner is not accessible.");
        if (RunnerApplicationProfiles.Normalize(runner.ApplicationProfile) == RunnerApplicationProfiles.MachineManagement)
            throw new UnauthorizedAccessException("Raw workflow and custom-step execution is disabled on Machine Management Runners.");
        if (RunnerCapabilities.RequiresAdministrator(runner.CapabilitiesJson, runner.Purpose)
            && !await IsAdministratorAsync(db, actor, token))
            throw new UnauthorizedAccessException("Only administrators may execute on Infrastructure Runners.");
        if (!RunnerCapabilities.Includes(runner.CapabilitiesJson, RunnerCapabilities.WorkflowExecution, runner.Purpose))
            throw new InvalidOperationException("Runner does not support workflow execution.");
        if (!runner.LastHeartbeatAtUtc.HasValue
            || runner.LastHeartbeatAtUtc < DateTime.UtcNow - RunnerPresenceEvaluator.OfflineThreshold)
            throw new InvalidOperationException("Runner is offline.");
        return actor;
    }

    internal static Task<bool> IsAdministratorAsync(ApplicationDbContext db, Guid actor, CancellationToken token)
        => db.UserRoles.Join(db.Roles, u => u.RoleId, r => r.Id, (u, r) => new { u.UserId, r.Name })
            .AnyAsync(r => r.UserId == actor && (r.Name == UserRole.Administrator || r.Name == UserRole.SystemAdministrator), token);
}
