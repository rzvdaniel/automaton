using Automaton.Api.Data;
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
        if (!RunnerProfileRules.IsValid(runner.Purpose, runner.ApplicationProfile))
            throw new UnauthorizedAccessException("Only Machine Management Runners are supported.");
        throw new UnauthorizedAccessException("Raw workflow and custom-step execution is disabled on Machine Management Runners.");
    }
}
