using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class ProjectCredentialResourceService(
    ApplicationDbContext dbContext,
    ProjectResourceAuthorizationService authorizationService,
    IAuditEventService auditEventService)
{
    private readonly Serilog.ILogger logger = Serilog.Log.ForContext<ProjectCredentialResourceService>();

    public async Task<ProjectApiKeyDto?> AddApiKeyToProjectAsync(
        Guid projectId,
        Guid apiKeyId,
        Guid userId,
        string? userName,
        CancellationToken cancellationToken)
    {
        if (!await authorizationService.CanManageResourcesAsync(projectId, userId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to add API key to project {ProjectId} without permission", userId, projectId);
            await authorizationService.LogAccessDeniedAsync(projectId, apiKeyId, userId, cancellationToken);
            return null;
        }

        var apiKey = await dbContext.ApiKeys
            .FirstOrDefaultAsync(key => key.Id == apiKeyId && key.ApiKeyUsers.Any(user => user.UserId == userId), cancellationToken);
        if (apiKey is null)
            throw new InvalidOperationException("API key not found or access denied");

        var existingLink = await dbContext.ProjectApiKeys
            .FirstOrDefaultAsync(link => link.ProjectId == projectId && link.ApiKeyId == apiKeyId, cancellationToken);
        if (existingLink is not null)
            throw new InvalidOperationException("API key is already linked to this project");

        var projectApiKey = new ProjectApiKey
        {
            ProjectId = projectId,
            ApiKeyId = apiKeyId,
            AddedAt = DateTime.UtcNow,
            AddedBy = userId
        };
        dbContext.ProjectApiKeys.Add(projectApiKey);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEventService.WriteAsync(
            action: "Linked",
            entityType: "ApiKey",
            entityId: apiKeyId,
            message: "API key linked to project",
            metadata: new { ProjectId = projectId, ResourceId = apiKeyId, ResourceType = "ApiKey", UserId = userId },
            cancellationToken: cancellationToken);

        return new ProjectApiKeyDto
        {
            ProjectId = projectId,
            ApiKeyId = apiKeyId,
            ApiKeyName = apiKey.Name,
            AddedAt = projectApiKey.AddedAt,
            AddedBy = userId,
            AddedByName = userName ?? userId.ToString()
        };
    }

    public async Task<bool> RemoveApiKeyFromProjectAsync(
        Guid projectId,
        Guid apiKeyId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await authorizationService.CanManageResourcesAsync(projectId, userId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to remove API key from project {ProjectId} without permission", userId, projectId);
            await authorizationService.LogAccessDeniedAsync(projectId, apiKeyId, userId, cancellationToken);
            return false;
        }

        var projectApiKey = await dbContext.ProjectApiKeys
            .FirstOrDefaultAsync(link => link.ProjectId == projectId && link.ApiKeyId == apiKeyId, cancellationToken);
        if (projectApiKey is null)
            return false;

        dbContext.ProjectApiKeys.Remove(projectApiKey);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEventService.WriteAsync(
            action: "Removed",
            entityType: "ApiKey",
            entityId: apiKeyId,
            message: "API key removed from project",
            metadata: new { ProjectId = projectId, ResourceId = apiKeyId, ResourceType = "ApiKey", UserId = userId },
            cancellationToken: cancellationToken);
        return true;
    }

    public async Task<IEnumerable<ProjectApiKeyDto>> ListProjectApiKeysAsync(
        Guid projectId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await HasProjectAccessAsync(projectId, userId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to list API keys in project {ProjectId} without access", userId, projectId);
            return Enumerable.Empty<ProjectApiKeyDto>();
        }

        return await dbContext.ProjectApiKeys
            .Where(link => link.ProjectId == projectId)
            .Include(link => link.ApiKey)
            .Include(link => link.AddedByUser)
            .Select(link => new ProjectApiKeyDto
            {
                ProjectId = link.ProjectId,
                ApiKeyId = link.ApiKeyId,
                ApiKeyName = link.ApiKey == null ? "Unavailable API key" : link.ApiKey.Name,
                AddedAt = link.AddedAt,
                AddedBy = link.AddedBy,
                AddedByName = link.AddedByUser != null ? link.AddedByUser.UserName ?? string.Empty : string.Empty
            })
            .OrderByDescending(link => link.AddedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ProjectAiApiKeyDto?> AddAiApiKeyToProjectAsync(
        Guid projectId,
        Guid aiApiKeyId,
        Guid userId,
        string? userName,
        CancellationToken cancellationToken)
    {
        if (!await authorizationService.CanManageResourcesAsync(projectId, userId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to add AI API key to project {ProjectId} without permission", userId, projectId);
            await authorizationService.LogAccessDeniedAsync(projectId, aiApiKeyId, userId, cancellationToken);
            return null;
        }

        var apiKey = await dbContext.AiApiKeys
            .FirstOrDefaultAsync(key => key.Id == aiApiKeyId && key.IsActive, cancellationToken);
        if (apiKey is null)
            throw new InvalidOperationException("AI API key not found");

        var existingLink = await dbContext.ProjectAiApiKeys
            .FirstOrDefaultAsync(link => link.ProjectId == projectId && link.AiApiKeyId == aiApiKeyId, cancellationToken);
        if (existingLink is not null)
            throw new InvalidOperationException("AI API key is already linked to this project");

        var projectAiApiKey = new ProjectAiApiKey
        {
            ProjectId = projectId,
            AiApiKeyId = aiApiKeyId,
            AddedAt = DateTime.UtcNow,
            AddedBy = userId
        };
        dbContext.ProjectAiApiKeys.Add(projectAiApiKey);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEventService.WriteAsync(
            action: "Linked",
            entityType: "AiApiKey",
            entityId: aiApiKeyId,
            message: "AI API key linked to project",
            metadata: new { ProjectId = projectId, ResourceId = aiApiKeyId, ResourceType = "AiApiKey", UserId = userId },
            cancellationToken: cancellationToken);

        return new ProjectAiApiKeyDto
        {
            ProjectId = projectId,
            AiApiKeyId = aiApiKeyId,
            AiApiKeyName = apiKey.Name,
            AddedAt = projectAiApiKey.AddedAt,
            AddedBy = userId,
            AddedByName = userName ?? userId.ToString()
        };
    }

    public async Task<bool> RemoveAiApiKeyFromProjectAsync(
        Guid projectId,
        Guid aiApiKeyId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await authorizationService.CanManageResourcesAsync(projectId, userId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to remove AI API key from project {ProjectId} without permission", userId, projectId);
            await authorizationService.LogAccessDeniedAsync(projectId, aiApiKeyId, userId, cancellationToken);
            return false;
        }

        var projectApiKey = await dbContext.ProjectAiApiKeys
            .FirstOrDefaultAsync(link => link.ProjectId == projectId && link.AiApiKeyId == aiApiKeyId, cancellationToken);
        if (projectApiKey is null)
            return false;

        dbContext.ProjectAiApiKeys.Remove(projectApiKey);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEventService.WriteAsync(
            action: "Removed",
            entityType: "AiApiKey",
            entityId: aiApiKeyId,
            message: "AI API key removed from project",
            metadata: new { ProjectId = projectId, ResourceId = aiApiKeyId, ResourceType = "AiApiKey", UserId = userId },
            cancellationToken: cancellationToken);
        return true;
    }

    public async Task<IEnumerable<ProjectAiApiKeyDto>> ListProjectAiApiKeysAsync(
        Guid projectId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await HasProjectAccessAsync(projectId, userId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to list AI API keys in project {ProjectId} without access", userId, projectId);
            return Enumerable.Empty<ProjectAiApiKeyDto>();
        }

        return await dbContext.ProjectAiApiKeys
            .Where(link => link.ProjectId == projectId)
            .Include(link => link.AiApiKey)
            .Include(link => link.AddedByUser)
            .Select(link => new ProjectAiApiKeyDto
            {
                ProjectId = link.ProjectId,
                AiApiKeyId = link.AiApiKeyId,
                AiApiKeyName = link.AiApiKey == null ? "Unavailable AI API key" : link.AiApiKey.Name,
                AddedAt = link.AddedAt,
                AddedBy = link.AddedBy,
                AddedByName = link.AddedByUser != null ? link.AddedByUser.UserName ?? string.Empty : string.Empty
            })
            .OrderByDescending(link => link.AddedAt)
            .ToListAsync(cancellationToken);
    }

    private Task<bool> HasProjectAccessAsync(Guid projectId, Guid userId, CancellationToken cancellationToken)
    {
        return dbContext.ProjectMembers.AnyAsync(
            member => member.ProjectId == projectId && member.UserId == userId && member.IsActive,
            cancellationToken);
    }
}
