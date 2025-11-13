using Automaton.Studio.Server.Data;
using Serilog;

namespace Automaton.Studio.Server.Services;

public class ProjectAuditService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserContextService _userContextService;
    private readonly Serilog.ILogger _logger;

    public ProjectAuditService(ApplicationDbContext dbContext, UserContextService userContextService)
    {
        _dbContext = dbContext;
        _userContextService = userContextService;
        _logger = Log.ForContext<ProjectAuditService>();
    }

    public async Task LogProjectCreatedAsync(Guid projectId, string projectName, CancellationToken cancellationToken = default)
    {
        _logger.Information("Audit: Project created {ProjectId} ({ProjectName}) by {UserId}", projectId, projectName, _userContextService.GetUserId());
        // Optionally persist to a dedicated audit table in the future
        await Task.CompletedTask;
    }

    public async Task LogProjectDeletedAsync(Guid projectId, string projectName, CancellationToken cancellationToken = default)
    {
        _logger.Information("Audit: Project deleted {ProjectId} ({ProjectName}) by {UserId}", projectId, projectName, _userContextService.GetUserId());
        await Task.CompletedTask;
    }

    public async Task LogMemberChangedAsync(Guid projectId, Guid memberUserId, string action, CancellationToken cancellationToken = default)
    {
        _logger.Information("Audit: Member {Action} in project {ProjectId} for user {MemberUserId} by {UserId}", action, projectId, memberUserId, _userContextService.GetUserId());
        await Task.CompletedTask;
    }

    public async Task LogResourceLinkedAsync(Guid projectId, Guid resourceId, string resourceType, CancellationToken cancellationToken = default)
    {
        _logger.Information("Audit: Resource linked {ResourceType} {ResourceId} to project {ProjectId} by {UserId}", resourceType, resourceId, projectId, _userContextService.GetUserId());
        await Task.CompletedTask;
    }

    public async Task LogAccessDeniedAsync(Guid projectId, Guid resourceId, Guid userId, CancellationToken cancellationToken = default)
    {
        _logger.Warning("Audit: Access denied to resource {ResourceId} in project {ProjectId} for user {UserId}", resourceId, projectId, userId);
        await Task.CompletedTask;
    }
}
