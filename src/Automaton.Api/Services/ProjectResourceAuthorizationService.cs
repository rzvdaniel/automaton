using Automaton.Api.Data;
using Automaton.Api.Enums;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class ProjectResourceAuthorizationService(
    ApplicationDbContext dbContext,
    IAuditEventService auditEventService)
{
    public async Task<bool> CanManageResourcesAsync(Guid projectId, Guid userId, CancellationToken cancellationToken)
    {
        var membership = await dbContext.ProjectMembers
            .FirstOrDefaultAsync(member =>
                member.ProjectId == projectId && member.UserId == userId && member.IsActive,
                cancellationToken);

        return membership?.Role is ProjectRole.ProjectAdmin or ProjectRole.Developer;
    }

    public Task LogAccessDeniedAsync(
        Guid projectId,
        Guid resourceId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return auditEventService.WriteAsync(
            action: "AccessDenied",
            entityType: "ProjectResource",
            entityId: resourceId,
            message: "Project resource access denied",
            metadata: new { ProjectId = projectId, ResourceId = resourceId, UserId = userId },
            outcome: "Denied",
            cancellationToken: cancellationToken);
    }
}
